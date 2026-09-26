const installTimeoutMs = 30000;
const activationTimeoutMs = 5000;
const operationTimeoutMs = 10000;
let registrationPromise;
let refreshPromise;
let refreshing = false;
let reloading = false;
const observedRegistrations = new WeakSet();
const promptedWorkers = new WeakSet();

function isDevelopment() {
    return ['localhost', '127.0.0.1'].includes(window.location.hostname) ||
        window.location.hostname.startsWith('192.168.');
}

function withTimeout(operation, timeoutMs, message) {
    return new Promise((resolve, reject) => {
        const timeout = window.setTimeout(() => reject(new Error(message)), timeoutMs);

        Promise.resolve(operation).then(
            value => {
                window.clearTimeout(timeout);
                resolve(value);
            },
            error => {
                window.clearTimeout(timeout);
                reject(error);
            });
    });
}

function waitForWorkerState(worker, acceptedStates, timeoutMs) {
    if (acceptedStates.includes(worker.state)) {
        return Promise.resolve();
    }

    return new Promise((resolve, reject) => {
        const timeout = window.setTimeout(() => {
            worker.removeEventListener('statechange', onStateChange);
            reject(new Error(`Service worker did not reach ${acceptedStates.join('/')} state.`));
        }, timeoutMs);

        function onStateChange() {
            if (worker.state === 'redundant') {
                window.clearTimeout(timeout);
                worker.removeEventListener('statechange', onStateChange);
                reject(new Error('The new service worker became redundant.'));
                return;
            }

            if (acceptedStates.includes(worker.state)) {
                window.clearTimeout(timeout);
                worker.removeEventListener('statechange', onStateChange);
                resolve();
            }
        }

        worker.addEventListener('statechange', onStateChange);
        onStateChange();
    });
}

function activateWorker(worker) {
    return new Promise((resolve, reject) => {
        const timeout = window.setTimeout(() => {
            navigator.serviceWorker.removeEventListener('controllerchange', onControllerChange);
            reject(new Error('Service worker activation timed out.'));
        }, activationTimeoutMs);

        function onControllerChange() {
            if (navigator.serviceWorker.controller !== worker) return;
            window.clearTimeout(timeout);
            navigator.serviceWorker.removeEventListener('controllerchange', onControllerChange);
            resolve();
        }

        navigator.serviceWorker.addEventListener('controllerchange', onControllerChange);
        worker.postMessage({ type: 'SKIP_WAITING' });
        onControllerChange();
    });
}

function reloadFromNetwork() {
    if (reloading) return;
    reloading = true;
    const url = new URL(window.location.href);
    url.searchParams.set('_appUpdate', Date.now().toString());
    window.location.replace(url.toString());
}

function offerUpdate(worker) {
    if (!worker || refreshing || reloading || promptedWorkers.has(worker)) return;
    promptedWorkers.add(worker);
    if (window.confirm('🎉 نسخه جدیدی از نرم‌افزار سفیر در دسترس است!\n\nآیا می‌خواهید برای اعمال تغییرات، صفحه را اکنون بروزرسانی کنید؟ (تنظیمات شما پاک نمی‌شود)')) {
        refreshing = true;
        activateWorker(worker).then(reloadFromNetwork).catch(error => {
            refreshing = false;
            console.error('Service worker activation failed.', error);
            window.alert('اعمال نسخه جدید کامل نشد. از منوی «بررسی و دریافت بروزرسانی» دوباره تلاش کنید.');
        });
    }
}

function observeRegistration(registration) {
    if (observedRegistrations.has(registration)) return;
    observedRegistrations.add(registration);
    const observeInstalling = () => {
        const worker = registration.installing;
        if (!worker) return;
        const onStateChange = () => {
            if (worker.state === 'installed' && navigator.serviceWorker.controller) {
                offerUpdate(worker);
            }
        };
        worker.addEventListener('statechange', onStateChange);
        onStateChange();
    };
    registration.addEventListener('updatefound', observeInstalling);
    observeInstalling();
    offerUpdate(registration.waiting);
}

async function getRegistration() {
    // Re-registering while an installation is running can queue behind that
    // installation. Reuse it so a slow download is not mistaken for failure.
    const existing = await withTimeout(
        navigator.serviceWorker.getRegistration(document.baseURI),
        operationTimeoutMs,
        'Service worker lookup timed out.');
    if (existing) {
        observeRegistration(existing);
        return existing;
    }
    if (!registrationPromise) {
        registrationPromise = navigator.serviceWorker.register('service-worker.js', {
            updateViaCache: 'none'
        }).then(registration => {
            observeRegistration(registration);
            return registration;
        }).catch(error => {
            registrationPromise = null;
            throw error;
        });
    }
    return withTimeout(registrationPromise, installTimeoutMs, 'Service worker registration timed out.');
}

export async function initialize() {
    if (!('serviceWorker' in navigator)) return;
    if (isDevelopment()) {
        const registration = await navigator.serviceWorker.getRegistration(document.baseURI);
        if (registration) await registration.unregister();
        return;
    }
    const registration = await getRegistration();
    if (!registration.waiting && !registration.installing && !refreshing) {
        await registration.update();
    }
}

async function refreshApplication() {
    if (!('serviceWorker' in navigator) || isDevelopment()) {
        reloadFromNetwork();
        return 'reloading';
    }

    const registration = await getRegistration();
    // A waiting/ installing worker already IS the update. Do not enqueue
    // another registration or update check behind its download.
    if (!registration.waiting && !registration.installing) {
        await withTimeout(registration.update(), operationTimeoutMs,
            'Service worker update check timed out.');
    }

    const candidate = registration.waiting || registration.installing;
    if (candidate) {
        await waitForWorkerState(candidate, ['installed', 'activated'], installTimeoutMs);
        if (candidate.state !== 'activated') {
            await activateWorker(candidate);
        }
    }
    // Another tab may already have activated the update while this page still
    // runs the old application. A manual refresh must load the active version.
    reloadFromNetwork();
    return 'reloading';
}

export function refresh() {
    if (!refreshPromise && !refreshing) {
        refreshing = true;
        refreshPromise = refreshApplication().finally(() => {
            refreshPromise = null;
            refreshing = false;
        });
    }
    return refreshPromise;
}
