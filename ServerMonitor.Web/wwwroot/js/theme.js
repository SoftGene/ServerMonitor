// Theme handling.
//
// An explicit choice wins and is remembered. Without one, the system preference decides:
// someone whose desktop is dark should not be handed a white page on their first visit.
// Only three states exist here, and "no choice yet" is deliberately not the same as "light".
window.themeManager = {
    resolveTheme: function () {
        const saved = localStorage.getItem('theme');

        if (saved === 'light' || saved === 'dark') {
            return saved;
        }

        return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches
            ? 'dark'
            : 'light';
    },

    getTheme: function () {
        return window.themeManager.resolveTheme();
    },

    setTheme: function (theme) {
        document.documentElement.setAttribute('data-theme', theme);
        localStorage.setItem('theme', theme);
    },

    initTheme: function () {
        const theme = window.themeManager.resolveTheme();
        document.documentElement.setAttribute('data-theme', theme);
        return theme;
    }
};

// Applied before the page paints rather than from a component: waiting for the Blazor circuit
// would show a white flash first on a machine set to dark.
window.themeManager.initTheme();
