const installTimeoutMs = 30000;
const activationTimeoutMs = 5000;

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
            registration = await navigator.serviceWorker.register('service-worker.js', {
                updateViaCache: 'none'
            });

            await registration.update();

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

    await clearApplicationCaches();

    if (registration) {
        await registration.unregister();
    }

    reloadFromNetwork();
}
