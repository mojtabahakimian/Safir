// Client\wwwroot\js\cleanup.js
window.appCleanup = {
    clearAllBrowserData: async function () {
        console.log("Starting browser data cleanup...");

        // Clear localStorage
        try {
            localStorage.clear();
            console.log("localStorage cleared.");
        } catch (e) {
            console.error("Error clearing localStorage:", e);
        }

        // Clear sessionStorage
        try {
            sessionStorage.clear();
            console.log("sessionStorage cleared.");
        } catch (e) {
            console.error("Error clearing sessionStorage:", e);
        }

        // Clear Cache Storage (for Service Worker caches)
        if ('caches' in window) {
            try {
                const cacheNames = await caches.keys();
                for (const name of cacheNames) {
                    await caches.delete(name);
                    console.log(`Cache '${name}' deleted.`);
                }
                console.log("All caches cleared.");
            } catch (e) {
                console.error("Error clearing caches:", e);
            }
        } else {
            console.log("Cache API not supported or available.");
        }

        // Unregister all Service Workers
        if ('serviceWorker' in navigator) {
            try {
                const registrations = await navigator.serviceWorker.getRegistrations();
                for (const registration of registrations) {
                    await registration.unregister();
                    console.log(`Service Worker unregistered: ${registration.scope}`);
                }
                console.log("All service workers unregistered.");
            } catch (e) {
                console.error("Error unregistering service workers:", e);
            }
        } else {
            console.log("Service Worker API not supported or available.");
        }

        console.log("Browser data cleanup complete.");
        return true; // Indicate success
    }
};