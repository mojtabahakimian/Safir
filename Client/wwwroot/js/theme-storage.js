window.themeStorage = {
    setTheme: function (isDarkMode) {
        localStorage.setItem('isDarkMode', isDarkMode);
    },
    getTheme: function () {
        return localStorage.getItem('isDarkMode') === 'true';
    }
};
