// Client\wwwroot\js\cleanup.js
window.appCleanup = {

    softRefresh: async function () {
        console.log("Starting Soft Refresh...");

        const protectedKeys = ['dbConnectionSettings', 'isDarkMode', 'authToken'];

        try {
            for (let i = localStorage.length - 1; i >= 0; i--) {
                const key = localStorage.key(i);
                if (!protectedKeys.includes(key)) {
                    localStorage.removeItem(key);
                }
            }
        } catch (e) { console.error("Error clearing localStorage:", e); }

        try { sessionStorage.clear(); } catch (e) { }

        if ('caches' in window) {
            try {
                const cacheNames = await caches.keys();
                for (const name of cacheNames) {
                    await caches.delete(name);
                }
            } catch (e) { }
        }

        if ('serviceWorker' in navigator) {
            try {
                const registrations = await navigator.serviceWorker.getRegistrations();
                for (const registration of registrations) {
                    await registration.unregister();
                }
            } catch (e) { }
        }

        return true;
    },

    factoryReset: async function () {
        console.log("Starting Factory Reset...");
        try { localStorage.clear(); } catch (e) { }
        try { sessionStorage.clear(); } catch (e) { }

        if ('caches' in window) {
            try {
                const cacheNames = await caches.keys();
                for (const name of cacheNames) {
                    await caches.delete(name);
                }
            } catch (e) { }
        }

        if ('serviceWorker' in navigator) {
            try {
                const registrations = await navigator.serviceWorker.getRegistrations();
                for (const registration of registrations) {
                    await registration.unregister();
                }
            } catch (e) { }
        }

        return true;
    }
};