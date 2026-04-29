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
    },

    // Writes a string to the OS clipboard. Returns true on success.
    // Falls back to a hidden <textarea> + execCommand path for older
    // browsers / non-secure contexts where navigator.clipboard is unavailable.
    copyToClipboard: async function (text) {
        if (text == null) return false;
        try {
            if (navigator.clipboard && window.isSecureContext) {
                await navigator.clipboard.writeText(String(text));
                return true;
            }
        } catch (_) { /* fall through to the legacy path */ }

        try {
            var ta = document.createElement('textarea');
            ta.value = String(text);
            ta.setAttribute('readonly', '');
            ta.style.position = 'fixed';
            ta.style.top = '-1000px';
            ta.style.left = '-1000px';
            document.body.appendChild(ta);
            ta.select();
            var ok = document.execCommand && document.execCommand('copy');
            document.body.removeChild(ta);
            return !!ok;
        } catch (_) {
            return false;
        }
    },

    // Re-parents an element directly under <body>, breaking it out of any
    // ancestor stacking context / overflow clipping (e.g. position: sticky +
    // overflow: hidden on the sidebar panel). No-op if already a body child.
    // Used by TeamQuickInfoPopover so its backdrop + card always float above
    // dashboard content and can never be clipped by the surrounding card.
    portalToBody: function (el) {
        if (!el || !document.body) return;
        if (el.parentElement === document.body) return;
        try { document.body.appendChild(el); } catch (_) { /* ignore */ }
    },

    // Returns the viewport-relative bounding rect of an element. Used to
    // anchor floating popovers (e.g. TeamQuickInfoPopover) outside scroll
    // containers via position: fixed.
    getBoundingRect: function (el) {
        if (!el || typeof el.getBoundingClientRect !== 'function') return null;
        var r = el.getBoundingClientRect();
        return {
            top:    r.top,
            left:   r.left,
            right:  r.right,
            bottom: r.bottom,
            width:  r.width,
            height: r.height,
            viewportWidth:  window.innerWidth  || document.documentElement.clientWidth,
            viewportHeight: window.innerHeight || document.documentElement.clientHeight
        };
    }

};
