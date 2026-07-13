const installTimeoutMs = 30000;
const activationTimeoutMs = 5000;
const operationTimeoutMs = 10000;
const recoveryTimeoutMs = 1000;
const cleanupTimeoutMs = 2000;

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
    });
}

function waitForControllerChange(timeoutMs) {
    return new Promise(resolve => {
        const timeout = window.setTimeout(() => {
            navigator.serviceWorker.removeEventListener('controllerchange', onControllerChange);
            resolve();
        }, timeoutMs);

        function onControllerChange() {
            window.clearTimeout(timeout);
            navigator.serviceWorker.removeEventListener('controllerchange', onControllerChange);
            resolve();
        }

        navigator.serviceWorker.addEventListener('controllerchange', onControllerChange);
    });
}

function reloadFromNetwork() {
    const url = new URL('./', document.baseURI);
    url.searchParams.set('_appUpdate', Date.now().toString());
    window.location.replace(url.toString());
}

async function clearApplicationCaches() {
    if (!('caches' in window)) {
        return;
    }

    const cacheNames = await caches.keys();
    await Promise.all(cacheNames.map(cacheName => caches.delete(cacheName)));
}

export async function refresh() {
    let registration = null;

    if ('serviceWorker' in navigator) {
        try {
            registration = await withTimeout(
                navigator.serviceWorker.register('service-worker.js', {
                    updateViaCache: 'none'
                }),
                operationTimeoutMs,
                'Service worker registration timed out.');

            await withTimeout(
                registration.update(),
                operationTimeoutMs,
                'Service worker update check timed out.');

            const candidate = registration.waiting || registration.installing;
            if (candidate && !['installed', 'activated'].includes(candidate.state)) {
                await waitForWorkerState(candidate, ['installed', 'activated'], installTimeoutMs);
            }

            if (registration.waiting) {
                const controllerChanged = waitForControllerChange(activationTimeoutMs);
                registration.waiting.postMessage({ type: 'SKIP_WAITING' });
                await controllerChanged;
                reloadFromNetwork();
                return;
            }

            if (candidate?.state === 'activated') {
                reloadFromNetwork();
                return;
            }
        } catch (error) {
            console.warn('Service worker update check failed. Falling back to a clean reload.', error);
        }
    }

    if (!registration && 'serviceWorker' in navigator) {
        registration = await withTimeout(
            navigator.serviceWorker.getRegistration(),
            recoveryTimeoutMs,
            'Service worker registration recovery timed out.')
            .catch(error => {
                console.warn('Service worker registration recovery failed. Continuing cleanup.', error);
                return null;
            });
    }

    try {
        const cleanupOperations = [clearApplicationCaches()];

        if (registration) {
            cleanupOperations.push(registration.unregister());
        }

        await withTimeout(
            Promise.all(cleanupOperations),
            cleanupTimeoutMs,
            'Application cache cleanup timed out.');
    } catch (error) {
        console.warn('Application cache cleanup failed. Reloading from the network.', error);
    }

    reloadFromNetwork();
}
