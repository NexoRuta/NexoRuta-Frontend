const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require(process.env.NEXORUTA_PLAYWRIGHT_MODULE);

(async () => {
    const base = process.env.DESTINATION_BASE_URL;
    const out = process.env.NEXORUTA_DESTINATION_BROWSER_DIR;
    const fullStack = process.env.DESTINATION_FULLSTACK === 'true';
    assert.match(base, /^http:\/\/(localhost|127\.0\.0\.1):\d+\/$/);
    await fs.mkdir(out, { recursive: true });
    const version = '3.33.0';
    const [sdk, css] = await Promise.all(['js', 'css'].map(async ext => {
        const response = await fetch(`https://api.mapbox.com/mapbox-gl-js/v${version}/mapbox-gl.${ext}`);
        assert.equal(response.status, 200); return response.text();
    }));
    const browser = await chromium.launch({ headless: true, args: ['--enable-webgl', '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    const external = [], errors = [], markerChecks = [], controlledSdkRequests = [], lifecycleChecks = [];
    let finalPoint;
    page.on('pageerror', e => errors.push(e.message));
    await page.route('**/*', async route => {
        const url = route.request().url();
        if (new URL(url).origin === new URL(base).origin && /^\/js\/destino-mapbox(?:\.[a-z0-9]+)?\.js$/.test(new URL(url).pathname)) {
            const response = await route.fetch();
            const source = (await response.text())
                .replace('export function conectarTeclado(', 'function conectarTecladoOriginal(')
                .replace('export async function crear(', 'async function crearOriginal(');
            return route.fulfill({ response, body: source + `
async function lifecycleBarrier(phase) {
    const state = globalThis.__destinationLifecycle;
    if (state?.phase !== phase) return;
    state.started = true;
    await new Promise(resolve => state.release = resolve);
}
await lifecycleBarrier('import');
export async function conectarTeclado(...args) {
    const binding = conectarTecladoOriginal(...args);
    const state = globalThis.__destinationLifecycle;
    if (state) state.keyboards++;
    await lifecycleBarrier('conectarTeclado');
    return { dispose() { if (state) state.keyboardDisposals++; binding.dispose(); } };
}
export async function crear(...args) {
    const map = await crearOriginal(...args);
    const state = globalThis.__destinationLifecycle;
    if (state) state.maps++;
    await lifecycleBarrier('crear');
    return { ...map, dispose() { if (state) state.mapDisposals++; map.dispose(); } };
}` });
        }
        if (url === `https://api.mapbox.com/mapbox-gl-js/v${version}/mapbox-gl.css`)
            return route.fulfill({ contentType: 'text/css', body: css });
        const parsed = new URL(url);
        if (parsed.hostname === 'events.mapbox.com' || (parsed.hostname === 'api.mapbox.com' && parsed.pathname === '/map-sessions/v1')) {
            controlledSdkRequests.push({host:parsed.hostname,path:parsed.pathname});
            return route.fulfill({status:200,contentType:'application/json',body:'{}'});
        }
        if (url.startsWith('blob:') || new URL(url).origin === new URL(base).origin) return route.continue();
        external.push(new URL(url).hostname); return route.abort();
    });
    // Actual SDK/Marker; only the style is replaced with an empty local style to avoid tiles/billing.
    await page.addInitScript({ content: sdk + `
const RealMap = mapboxgl.Map;
        mapboxgl.Map = class extends RealMap {
            constructor(options) { super({ ...options, style: { version: 8, sources: {}, layers: [] } }); globalThis.__destinationRealMap = this; }
            remove() { if (globalThis.__destinationLifecycle) globalThis.__destinationLifecycle.removals++; return super.remove(); }
        };` });
    const waitMap = async () => {
        await page.locator('.mapboxgl-canvas').waitFor();
        await page.waitForFunction(() => globalThis.__destinationRealMap?.loaded() && !globalThis.__destinationRealMap.isMoving());
    };
    async function visibleMarker(label) {
        await page.locator('.mapboxgl-marker').waitFor();
        await page.waitForFunction(() => {
            const e = document.querySelector('.mapboxgl-marker'); const m = document.querySelector('.nr-destination-map');
            if (!e || !m) return false;
            const a = e.getBoundingClientRect(), b = m.getBoundingClientRect();
            return getComputedStyle(e).position === 'absolute' && a.width > 0 && a.height > 0 && a.left >= b.left && a.right <= b.right && a.top >= b.top && a.bottom <= b.bottom;
        });
        markerChecks.push({ label, width: await page.evaluate(() => innerWidth), actualSdk: version, visible: true });
    }
    async function clickPoint(lat = -34.91, lon = -56.21) {
        await waitMap();
        await page.locator('.mapboxgl-canvas').scrollIntoViewIfNeeded();
        // Physical mouse coordinates are rounded to pixels. At zoom 17 a pixel is
        // smaller than the existing 1e-5 degree assertion, including fractional layouts.
        await page.evaluate(({lat,lon}) => globalThis.__destinationRealMap.jumpTo({ center: [lon,lat], zoom: 17 }), {lat,lon});
        await page.waitForFunction(() => globalThis.__destinationRealMap.loaded() && !globalThis.__destinationRealMap.isMoving());
        await page.evaluate(() => globalThis.__destinationRealMap.once('click', event => {
            globalThis.__expectedClickPoint = { lat: event.lngLat.lat, lon: event.lngLat.lng };
        }));
        const box = await page.locator('.mapboxgl-canvas').boundingBox();
        await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
        finalPoint = await page.evaluate(() => globalThis.__expectedClickPoint);
        assert.ok(Math.abs(finalPoint.lat - lat) < 0.00001 && Math.abs(finalPoint.lon - lon) < 0.00001, JSON.stringify({expected:{lat,lon},actual:finalPoint,center:await page.evaluate(()=>globalThis.__destinationRealMap.getCenter())}));
        await visibleMarker('map click');
    }
    async function dragMarker() {
        await page.locator('.mapboxgl-marker').scrollIntoViewIfNeeded();
        const box = await page.locator('.mapboxgl-marker').boundingBox();
        await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
        await page.mouse.down(); await page.mouse.move(box.x + box.width / 2 + 35, box.y + box.height / 2 + 20, {steps: 8}); await page.mouse.up();
        await page.waitForFunction(before => {
            const marker = document.querySelector('.mapboxgl-marker').getBoundingClientRect();
            return Math.abs(marker.x - before.x) + Math.abs(marker.y - before.y) > 5;
        }, box);
        await visibleMarker('marker drag');
    }
    async function assertSaved() {
        await page.getByText('Ubicación confirmada y guardada.', {exact:true}).waitFor();
        assert.equal(await page.locator('#destino-revisado').count(), 0);
        assert.equal(await page.getByRole('button', {name:/^Confirmar (nueva )?ubicación$/}).count(), 0);
        await visibleMarker('saved/reopened');
    }
    try {
        await page.goto(base + 'ingresar');
        await page.locator('#Email').fill(process.env.DESTINATION_EMAIL || 'commerce@example.test');
        await page.locator('#Password').fill(process.env.DESTINATION_PASSWORD || 'correct-password');
        await Promise.all([page.waitForNavigation(), page.getByRole('button', { name: 'Ingresar', exact: true }).click()]);
        const url = base + 'envios/' + (process.env.DESTINATION_SHIPMENT || '00000000-0000-0000-0000-000000000999') + '/destino';
        if (!fullStack) {
            for (const phase of ['import', 'conectarTeclado', 'crear']) {
                // A fresh document gives the import phase a real, uncached module initialization.
                await page.goto(base + 'envios');
                await page.waitForFunction(() => typeof Blazor?.navigateTo === 'function');
                await page.evaluate(({ phase, url }) => {
                    globalThis.__destinationLifecycle = { phase, started: false, keyboards: 0, keyboardDisposals: 0, maps: 0, mapDisposals: 0, removals: 0 };
                    Blazor.navigateTo(url);
                }, { phase, url });
                await page.waitForFunction(() => globalThis.__destinationLifecycle.started);
                await page.evaluate(() => Blazor.navigateTo('/envios'));
                await page.locator('.nr-destination-picker').waitFor({ state: 'detached' });
                await page.evaluate(() => globalThis.__destinationLifecycle.release());
                const expected = { keyboards: phase === 'import' ? 0 : 1, maps: phase === 'crear' ? 1 : 0 };
                await page.waitForFunction(expected => {
                    const state = globalThis.__destinationLifecycle;
                    return state.keyboardDisposals === expected.keyboards && state.mapDisposals === expected.maps;
                }, expected);
                const result = await page.evaluate(() => {
                    const { release, ...state } = globalThis.__destinationLifecycle;
                    return { ...state, canvases: document.querySelectorAll('.mapboxgl-canvas').length };
                });
                assert.equal(result.keyboards, expected.keyboards);
                assert.equal(result.maps, expected.maps);
                assert.equal(result.removals, expected.maps);
                assert.equal(result.canvases, 0);
                lifecycleChecks.push(result);
                // Re-entry must create one usable selector, not reuse disposed bindings.
                await page.evaluate(url => { globalThis.__destinationLifecycle = null; Blazor.navigateTo(url); }, url);
                await waitMap();
                assert.equal(await page.locator('.mapboxgl-canvas').count(), 1);
            }
        }
        await page.goto(url);
        await waitMap();
        const input = page.getByRole('combobox', { name: 'Buscar dirección' });
        await input.fill('ab'); await page.waitForTimeout(400);
        assert.equal(await page.getByRole('option').count(), 0);
        await input.fill('Primera');
        await page.getByRole('option', { name: 'Primera 123' }).waitFor();
        await input.press('ArrowDown');
        await page.waitForFunction(() => document.querySelector('#destino-busqueda')?.getAttribute('aria-activedescendant') === 'destino-opcion-0');
        await input.press('Enter');
        await page.getByText('Sugerencia seleccionada · pendiente de confirmar.',{exact:true}).waitFor();
        assert.equal(await page.getByRole('option').count(),0);
        await visibleMarker('suggestion selected');
        await page.screenshot({path:path.join(out,'suggested-real-1440.png'),fullPage:true});
        assert.equal(await page.getByText('Vista previa temporal:',{exact:false}).count(),0);
        assert.equal(await page.getByRole('button',{name:'Descartar vista previa temporal'}).count(),0);
        await page.locator('#destino-revisado').check();
        await page.setViewportSize({width:320,height:1000});
        await visibleMarker('suggestion selected after 320px resize');
        await page.waitForFunction(() => !document.querySelector('.nr-destination-primary')?.disabled);
        assert.equal(await page.getByRole('button',{name:/^Confirmar (nueva )?ubicación$/}).isEnabled(),true);
        await page.screenshot({path:path.join(out,'selected-320.png'),fullPage:true});
        await page.getByRole('button',{name:/^Confirmar (nueva )?ubicación$/}).click();
        await page.getByText('Ubicación confirmada. El punto quedó listo para su planificación.',{exact:true}).waitFor();
        await page.reload();
        await waitMap();
        await page.getByRole('heading',{name:'Primera 123',exact:true}).waitFor();
        await assertSaved();
        await input.fill('Cambio');await page.getByRole('option',{name:'Cambio 123'}).click();
        await visibleMarker('second suggestion');
        await dragMarker();
        await page.locator('#destino-revisado').check();
        await page.getByRole('button',{name:'Confirmar nueva ubicación',exact:true}).click();
        await page.getByText('Ubicación confirmada. El punto quedó listo para su planificación.',{exact:true}).waitFor();
        await page.reload();await waitMap();
        await page.getByRole('heading',{name:'Cambio 123',exact:true}).waitFor();
        await assertSaved();
        if(fullStack){
            await fs.writeFile(path.join(out,'conflict.ready'),'ready');
            for(let i=0;;i++){
                try{await fs.access(path.join(out,'conflict.applied'));break;}catch{}
                assert.ok(i<200,'fixture concurrency handshake timed out');await page.waitForTimeout(50);
            }
            await clickPoint(-34.905, -56.205);
            await page.locator('#destino-revisado').check();
            await page.getByRole('button',{name:'Confirmar nueva ubicación',exact:true}).click();
            await page.getByRole('alert').filter({hasText:'El envío cambió mientras revisabas'}).waitFor();
            assert.equal(await page.locator('#destino-revisado').isChecked(),false);
            await page.waitForTimeout(500);
            await page.screenshot({path:path.join(out,'conflict-320.png'),fullPage:true});
            await page.locator('#destino-revisado').check();
            await page.getByRole('button',{name:'Confirmar nueva ubicación',exact:true}).click();
            await page.getByText('Ubicación confirmada. El punto quedó listo para su planificación.',{exact:true}).waitFor();
            await page.reload();await waitMap();
            await page.getByRole('heading',{name:'Cambio 123',exact:true}).waitFor();
        }
        if (fullStack) {
            await input.fill('Fallo');
            await page.getByRole('alert').filter({ hasText: 'no está habilitada' }).waitFor();
        }
        await input.fill('Cuota');
        await page.getByRole('alert').filter({ hasText: 'Se agotó la cuota' }).waitFor();
        await clickPoint();
        await page.locator('#destino-revisado').check();
        await page.getByRole('button', { name: 'Confirmar nueva ubicación', exact: true }).click();
        await page.getByText('Ubicación confirmada. El punto quedó listo para su planificación.', { exact: true }).waitFor();
        await page.reload(); await waitMap(); await assertSaved();
        for (const width of [1440, 320]) {
            await page.setViewportSize({width,height:1000}); await assertSaved();
            await page.screenshot({path:path.join(out,`saved-real-${width}.png`),fullPage:true});
        }
        if (!fullStack) {
            await input.fill('Fallo');
            await page.getByRole('alert').filter({ hasText: 'no está habilitada' }).waitFor();
        }
        for (const width of [1440, 320]) {
            await page.setViewportSize({ width, height: 1000 });
            assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'overflow ' + width);
            await input.focus();
            assert.ok(await input.evaluate(e => document.activeElement === e));
            await page.screenshot({ path: path.join(out, `destination-${width}.png`), fullPage: true });
        }
        assert.deepEqual(errors, []);
        assert.deepEqual(external, []);
        await fs.writeFile(path.join(out,'real-marker-result.json'),JSON.stringify({actualSdk:version,markerChecks,lifecycleChecks,finalPoint,controlledSdkRequests,unexpectedExternalRequests:external,geocodingTilesTelemetryNetworkCalls:0,staticSdkCssDownloads:2,passed:true},null,2));
        console.log('PASS: actual SDK/CSS suggestion marker, physical click/drag, save/reopen and confirmed state without duplicate save, keyboard/debounce, quota/error/manual fallback, 1440/320px. SDK/CSS assets downloaded; style and telemetry controlled; zero geocoding/tiles/telemetry network calls.');
    } catch (error) {
        await page.screenshot({ path: path.join(out, 'failure.png'), fullPage: true });
        console.error((await page.locator('body').innerText()).slice(-4000));
        throw error;
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
