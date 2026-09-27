window.themeStorage = {
    setTheme: function (isDarkMode) {
        var dark = isDarkMode === true || isDarkMode === 'true';
        localStorage.setItem('isDarkMode', dark);
        document.documentElement.classList.toggle('dark-theme', dark);
    },
    getTheme: function () {
        return localStorage.getItem('isDarkMode') === 'true';
    },
    setScheme: function (scheme) {
        var s = (scheme || 'classic').toString().toLowerCase();
        localStorage.setItem('themeScheme', s);
        document.documentElement.setAttribute('data-theme-scheme', s);
    },
    getScheme: function () {
        return localStorage.getItem('themeScheme') || 'classic';
    }
};
