window.navPill = {
    update: function () {
        const pill = document.getElementById('navPill');
        const indicator = document.getElementById('navIndicator');
        if (!pill || !indicator) return;

        const active = pill.querySelector('.nav-link.active');
        if (!active) {
            indicator.style.opacity = '0';
            return;
        }

        const pillRect = pill.getBoundingClientRect();
        const activeRect = active.getBoundingClientRect();

        const left = activeRect.left - pillRect.left;
        const width = activeRect.width;

        indicator.style.opacity = '1';
        indicator.style.width = width + 'px';
        indicator.style.transform = 'translateX(' + left + 'px)';
    }
};