// Phosphor flash on values when data refreshes.
// Watches for text changes inside .metric-value / .ch-val and restarts the
// .value-flash CSS animation (see app.css).
window.liveFlash = (function () {
    let observer = null;

    function onMutations(mutations) {
        const seen = new Set();
        for (const m of mutations) {
            let node = m.target;
            if (node.nodeType === Node.TEXT_NODE) node = node.parentElement;
            const el = node && node.closest ? node.closest('.metric-value, .ch-val') : null;
            if (el && !seen.has(el)) {
                seen.add(el);
                el.classList.remove('value-flash');
                void el.offsetWidth; // restart the animation
                el.classList.add('value-flash');
            }
        }
    }

    function start() {
        if (observer) return;
        observer = new MutationObserver(onMutations);
        observer.observe(document.body, { subtree: true, childList: true, characterData: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }

    return { start: start };
})();
