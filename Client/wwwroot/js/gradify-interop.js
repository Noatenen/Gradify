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

    // Jump to a page anchor — block:'start', unlike scrollIntoView above, which
    // uses 'nearest' and therefore does nothing while any part of the target is
    // still on screen. That is right for "reveal this row" and wrong for "jump
    // to this section", which is what a fragment link promises. The target's own
    // CSS scroll-margin-top supplies the offset.
    scrollToAnchor: function (id) {
        var el = document.getElementById(id);
        if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    },

    // Bring a just-opened in-flow popover fully into view.
    //
    // block:'end' rather than the 'nearest' above, and the difference matters:
    // 'nearest' does nothing while ANY part of the target is on screen, which is
    // exactly the state a panel is in the moment it opens under its trigger —
    // its first row is visible and the rest is below the fold. Aligning the
    // panel's END with the scroller's end reveals the whole thing.
    //
    // INSTANT, unlike the two above. A panel opens under the pointer and the
    // user is already reaching for it — animating the page out from under that
    // reach makes them chase it, and a smooth scroll is also frozen outright in
    // a background tab, which would leave the panel half off-screen with no
    // way to tell. The adjustment is small; it should simply be done.
    //
    // Silent when the id is gone: the panel may have closed between the render
    // that scheduled this and the call itself.
    revealPopover: function (id) {
        var el = document.getElementById(id);
        if (el) el.scrollIntoView({ behavior: 'instant', block: 'end' });
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
    },

    // Prevents the page from scrolling behind an open modal.
    // Uses a depth counter so multiple modals stacking never double-unlock.
    scrollLock: (function () {
        var depth = 0;
        return {
            lock: function () {
                if (++depth === 1) document.body.style.overflow = 'hidden';
            },
            unlock: function () {
                if (depth > 0 && --depth === 0) document.body.style.overflow = '';
            }
        };
    })()

};
