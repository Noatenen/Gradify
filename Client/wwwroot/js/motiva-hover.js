/*
 * motiva-hover.js — pointer-aware ambient hover for interactive cards.
 *
 * Sets --mx / --my on the interactive card under the pointer so the soft
 * radial wash defined in CSS follows it. Purely decorative: the CSS
 * defaults both variables to 50%, so with this file absent (or with JS
 * disabled) the effect degrades to a centred glow and nothing breaks.
 *
 * ONE delegated listener on the document, not one per card:
 *   • no Blazor JS interop, so no IAsyncDisposable plumbing per component
 *     and no handlers to leak when a card re-renders,
 *   • survives SPA navigation, because it binds to the document rather
 *     than to any element the router replaces.
 *
 * Writes are throttled to one per animation frame — a pointermove can fire
 * far more often than the screen repaints, and setting a custom property
 * on every event is what makes this kind of effect feel heavy.
 */
(() => {
    'use strict';

    const SELECTOR = '.m-card-interactive';
    let pending = null;

    // Respect the OS setting: no pointer tracking when motion is reduced.
    // The CSS still shows the static centred wash on hover.
    const reduced = window.matchMedia?.('(prefers-reduced-motion: reduce)');
    if (reduced?.matches) return;

    function apply() {
        pending = null;
        const { card, x, y } = current;
        if (!card) return;
        card.style.setProperty('--mx', x + '%');
        card.style.setProperty('--my', y + '%');
    }

    let current = { card: null, x: 50, y: 50 };
    let lastCard = null;

    document.addEventListener('pointermove', (e) => {
        // `pointermove` fires for touch and pen too; this is a hover
        // affordance, so only a real hovering device drives it.
        if (e.pointerType !== 'mouse') return;

        const card = e.target instanceof Element ? e.target.closest(SELECTOR) : null;

        if (card !== lastCard && lastCard) {
            lastCard.style.removeProperty('--mx');
            lastCard.style.removeProperty('--my');
        }
        lastCard = card;
        if (!card) return;

        const r = card.getBoundingClientRect();
        current = {
            card,
            x: Math.round(((e.clientX - r.left) / r.width) * 100),
            y: Math.round(((e.clientY - r.top) / r.height) * 100),
        };

        if (pending === null) pending = requestAnimationFrame(apply);
    }, { passive: true });

    // Leaving the window mid-hover would otherwise freeze the wash where
    // the pointer last was.
    document.addEventListener('pointerleave', () => {
        if (!lastCard) return;
        lastCard.style.removeProperty('--mx');
        lastCard.style.removeProperty('--my');
        lastCard = null;
    }, { passive: true });
})();
