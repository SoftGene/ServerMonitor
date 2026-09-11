// A small wireframe cube beside the machine's name on its dashboard. It turns while readings are
// arriving, and stops and fades when they are not.
//
// Drawn in plain SVG from a rotation computed here, not with CSS 3D transforms. The CSS cube
// (perspective, preserve-3d, translateZ) was composited on a layer of its own, and in Chrome that
// layer flickered green across the header whenever the sticky top bar overlapped it. SVG paint needs
// no such layer. Computing the projection also allows what the CSS version could not do: edges further
// away are drawn fainter, which is most of what makes a wireframe read as solid.
(function () {
    const SIZE = 26;
    const HALF = SIZE / 2;
    const DISTANCE = 6;        // camera distance, in half-widths of the cube
    const SCALE = 6.4;         // pixels per half-width at the depth of the cube's centre
    const TILT = -0.38;        // radians: seen a little from above
    const TURN_SECONDS = 16;   // one full turn while readings arrive

    const VERTICES = [];
    for (const x of [-1, 1]) {
        for (const y of [-1, 1]) {
            for (const z of [-1, 1]) {
                VERTICES.push([x, y, z]);
            }
        }
    }

    // Two corners share an edge when they differ in exactly one coordinate.
    const EDGES = [];
    for (let a = 0; a < VERTICES.length; a++) {
        for (let b = a + 1; b < VERTICES.length; b++) {
            const differing = VERTICES[a].filter((value, i) => value !== VERTICES[b][i]).length;
            if (differing === 1) {
                EDGES.push([a, b]);
            }
        }
    }

    const NEAREST = -Math.sqrt(3);
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');

    class ServerMonitorCube extends HTMLElement {
        static get observedAttributes() {
            return ['data-flowing'];
        }

        constructor() {
            super();
            this.angle = 0.6;
            this.frame = 0;
            this.last = 0;
            this.tick = this.tick.bind(this);
            this.onMotionPreference = () => this.sync();

            // A shadow root, so Blazor, which renders this element with no children, never touches the
            // lines inside it on its next render.
            const svgNs = 'http://www.w3.org/2000/svg';
            const svg = document.createElementNS(svgNs, 'svg');
            svg.setAttribute('viewBox', '0 0 ' + SIZE + ' ' + SIZE);
            svg.setAttribute('width', '100%');
            svg.setAttribute('height', '100%');
            svg.style.display = 'block';
            svg.style.overflow = 'visible';

            this.lines = EDGES.map(() => {
                const line = document.createElementNS(svgNs, 'line');
                line.setAttribute('stroke', 'currentColor');
                line.setAttribute('stroke-width', '1');
                line.setAttribute('stroke-linecap', 'round');
                svg.appendChild(line);
                return line;
            });

            this.attachShadow({ mode: 'open' }).appendChild(svg);
        }

        connectedCallback() {
            reducedMotion.addEventListener('change', this.onMotionPreference);
            this.draw();
            this.sync();
        }

        disconnectedCallback() {
            reducedMotion.removeEventListener('change', this.onMotionPreference);
            this.stop();
        }

        attributeChangedCallback() {
            this.sync();
        }

        sync() {
            const flowing = this.getAttribute('data-flowing') === 'true';

            if (this.isConnected && flowing && !reducedMotion.matches) {
                this.start();
            } else {
                this.stop();
            }
        }

        start() {
            if (this.frame) {
                return;
            }

            this.last = 0;
            this.frame = requestAnimationFrame(this.tick);
        }

        stop() {
            if (!this.frame) {
                return;
            }

            cancelAnimationFrame(this.frame);
            this.frame = 0;
        }

        tick(now) {
            // Elapsed time rather than a step per frame, so it turns at the same speed at 60 Hz and at
            // 144 Hz, and a tab coming back from the background does not spin to catch up.
            if (this.last) {
                const seconds = Math.min((now - this.last) / 1000, 0.1);
                this.angle = (this.angle + seconds * 2 * Math.PI / TURN_SECONDS) % (2 * Math.PI);
            }

            this.last = now;
            this.draw();
            this.frame = requestAnimationFrame(this.tick);
        }

        draw() {
            const cosY = Math.cos(this.angle);
            const sinY = Math.sin(this.angle);
            const cosX = Math.cos(TILT);
            const sinX = Math.sin(TILT);

            const projected = VERTICES.map(([x, y, z]) => {
                // Turn about the vertical axis, then tilt towards the viewer.
                const turnedX = x * cosY + z * sinY;
                const turnedZ = -x * sinY + z * cosY;
                const tiltedY = y * cosX - turnedZ * sinX;
                const depth = y * sinX + turnedZ * cosX;
                const perspective = DISTANCE / (DISTANCE + depth);

                return [HALF + turnedX * SCALE * perspective, HALF + tiltedY * SCALE * perspective, depth];
            });

            EDGES.forEach(([a, b], i) => {
                const from = projected[a];
                const to = projected[b];
                // 1 for an edge at the nearest possible depth, 0 at the furthest.
                const nearness = (-NEAREST - (from[2] + to[2]) / 2) / (-2 * NEAREST);
                const line = this.lines[i];

                line.setAttribute('x1', from[0].toFixed(2));
                line.setAttribute('y1', from[1].toFixed(2));
                line.setAttribute('x2', to[0].toFixed(2));
                line.setAttribute('y2', to[1].toFixed(2));
                line.setAttribute('stroke-opacity', (0.2 + 0.8 * nearness).toFixed(2));
            });
        }
    }

    if (!customElements.get('sm-cube')) {
        customElements.define('sm-cube', ServerMonitorCube);
    }
})();
