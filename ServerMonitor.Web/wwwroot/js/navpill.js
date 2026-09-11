// Moves the indicator under the active section of the top bar.
//
// It slides only when the active section changes. Placing it for the first time, or measuring the same
// link again once the web font has loaded, happens without motion. On every page load the indicator
// used to grow out of the bar's left edge half a second after the page had painted: the first update
// came only once the Blazor circuit was up, and it ran through the same transition as a click.
window.navPill = (function () {
    function update() {
        const pill = document.getElementById('navPill');
        const indicator = document.getElementById('navIndicator');
        if (!pill || !indicator) return;

        const active = pill.querySelector('.nav-link.active');
        if (!active) {
            indicator.style.opacity = '0';
            delete indicator.dataset.section;
            return;
        }

        const section = active.getAttribute('href');
        const slide = indicator.dataset.section !== undefined && indicator.dataset.section !== section;

        const pillRect = pill.getBoundingClientRect();
        const activeRect = active.getBoundingClientRect();

        if (!slide) {
            indicator.style.transition = 'none';
        }

        indicator.style.opacity = '1';
        indicator.style.width = activeRect.width + 'px';
        indicator.style.transform = 'translateX(' + (activeRect.left - pillRect.left) + 'px)';

        if (!slide) {
            void indicator.offsetWidth; // apply the position before the transition comes back
            indicator.style.transition = '';
        }

        indicator.dataset.section = section;
    }

    function start() {
        // The server has already marked the active link, so the indicator can be in place at the first
        // paint rather than waiting for the circuit.
        update();

        // IBM Plex Mono arrives after the first paint and changes the widths of the links.
        if (document.fonts && document.fonts.ready) {
            document.fonts.ready.then(update);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }

    return { update: update };
})();
