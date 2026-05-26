window.appDiagnostics = {
    getDiagnosticsInfo: function () {
        return {
            browserInfo: navigator.userAgent,
            operatingSystem: navigator.platform,
            screenSize: window.screen.width + "x" + window.screen.height,
            userAgent: navigator.userAgent,
            pageUrl: window.location.href,
            route: window.location.pathname
        };
    }
};
