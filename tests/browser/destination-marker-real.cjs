// Real Mapbox SDK and CSS; empty local style, no tiles/geocoding/provider requests.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const http = require('node:http');
const { chromium } = require(process.env.NEXORUTA_PLAYWRIGHT_MODULE);
(async () => {
    const version = '3.33.0';
    const assets = await Promise.all(['js', 'css'].map(async ext => {
        const response = await fetch(`https://api.mapbox.com/mapbox-gl-js/v${version}/mapbox-gl.${ext}`);
        assert.equal(response.status, 200); return response.text();
    }));
    const source = await fs.readFile(path.join(__dirname, '../../src/NexoRuta.Commerce/wwwroot/js/destino-mapbox.js'), 'utf8');
    const server = http.createServer((req, res) => {
        const body = req.url === '/sdk.js' ? assets[0] : req.url === '/destino.js' ? source
            : '<!doctype html><style>body{margin:0}#map{width:100%;height:400px}</style><div id="map"></div><script src="/sdk.js"></script>';
        res.setHeader('Content-Type', req.url.endsWith('.js') ? 'text/javascript' : 'text/html'); res.end(body);
    });
    await new Promise(r => server.listen(0, '127.0.0.1', r));
    const base = `http://127.0.0.1:${server.address().port}`;
    const browser = await chromium.launch({ headless: true, args: ['--enable-webgl', '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
    const report = [];
    try {
        const page = await browser.newPage();
        await page.route('**/*', async route => {
            const url = route.request().url();
            if (url === `https://api.mapbox.com/mapbox-gl-js/v${version}/mapbox-gl.css`) return route.fulfill({ contentType: 'text/css', body: assets[1] });
            if (url.startsWith(base) || url.startsWith('blob:')) return route.continue();
            return route.abort();
        });
        for (const width of [1440, 320]) {
            await page.setViewportSize({ width, height: 800 });
            await page.goto(base);
            const result = await page.evaluate(async () => {
                const module = await import('/destino.js');
                const map = await module.crear(document.querySelector('#map'), 'pk.synthetic', { version: 8, sources: {}, layers: [] }, { invokeMethodAsync() { return Promise.resolve(); } }, true);
                map.mostrar(-34.9, -56.2);
                await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                const marker = document.querySelector('.mapboxgl-marker');
                const box = marker.getBoundingClientRect(), parent = document.querySelector('#map').getBoundingClientRect();
                const selected = { position: getComputedStyle(marker).position, width: box.width, height: box.height, inside: box.left >= parent.left && box.right <= parent.right && box.top >= parent.top && box.bottom <= parent.bottom };
                map.mostrar(-34.91, -56.21);
                await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                const movedBox = marker.getBoundingClientRect();
                const moved = movedBox.width === 27 && movedBox.height === 41 && movedBox.left >= parent.left && movedBox.right <= parent.right && movedBox.top >= parent.top && movedBox.bottom <= parent.bottom;
                map.dispose();
                const reopened = await module.crear(document.querySelector('#map'), 'pk.synthetic', { version: 8, sources: {}, layers: [] }, { invokeMethodAsync() { return Promise.resolve(); } }, true);
                reopened.mostrar(-34.91, -56.21);
                await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                const visible = document.querySelector('.mapboxgl-marker').getBoundingClientRect();
                return { selected, moved, reopened: visible.width > 0 && visible.height > 0 };
            });
            report.push({ width, ...result });
            assert.equal(result.selected.position, 'absolute', JSON.stringify(report));
            assert.ok(result.selected.width > 0 && result.selected.height > 0 && result.selected.inside, JSON.stringify(report));
            assert.ok(result.reopened);
            assert.ok(result.moved);
            if (process.env.NEXORUTA_DESTINATION_BROWSER_DIR) {
                await fs.mkdir(process.env.NEXORUTA_DESTINATION_BROWSER_DIR, { recursive: true });
                await page.screenshot({ path: path.join(process.env.NEXORUTA_DESTINATION_BROWSER_DIR, `marker-real-${width}.png`) });
            }
        }
        console.log(JSON.stringify({ passed: true, actualSdk: version, providerCalls: 0, report }));
    } finally { await browser.close(); await new Promise(r => server.close(r)); }
})().catch(e => { console.error(e); process.exitCode = 1; });
