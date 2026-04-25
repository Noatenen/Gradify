// Gradify JS interop helpers.
// Loaded before blazor.webassembly.js so every function is
// available the moment Blazor calls into JS.

(function () {
    // Apply saved theme immediately to avoid a flash on load.
    var saved = localStorage.getItem('gradify-theme');
    if (saved === 'dark')       document.documentElement.setAttribute('data-theme', 'dark');
    else if (saved === 'light') document.documentElement.setAttribute('data-theme', 'light');
    // 'system' / unset: no attribute — CSS @media query handles it.
})();

window.gradify = {

    setTheme: function (theme) {
        var html = document.documentElement;
        if (theme === 'dark')       html.setAttribute('data-theme', 'dark');
        else if (theme === 'light') html.setAttribute('data-theme', 'light');
        else                        html.removeAttribute('data-theme');
        try { localStorage.setItem('gradify-theme', theme); } catch (_) {}
    },

    getStoredTheme: function () {
        try { return localStorage.getItem('gradify-theme') || 'system'; } catch (_) { return 'system'; }
    },

    scrollIntoView: function (id) {
        var el = document.getElementById(id);
        if (el) el.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    },

    focusElement: function (el) {
        if (el) el.focus();
    }

};
