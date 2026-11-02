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
    const browser = await chromium.launch({ headless: true });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    const external = [], errors = [];
    page.on('pageerror', e => errors.push(e.message));
    await page.route('**/*', route => {
        if (new URL(route.request().url()).origin !== new URL(base).origin) {
            external.push(new URL(route.request().url()).hostname);
            return route.abort();
        }
        return route.continue();
    });
    await page.addInitScript(() => {
        // No real map, tiles, SDK, telemetry or geocoding requests in this test.
        class Map {
            constructor(options) {
                this.handlers = {};
                const button = document.createElement('button');
                button.type = 'button'; button.textContent = 'Mapa simulado: seleccionar punto';
                button.onclick = () => this.handlers.click?.({ lngLat: { lat: -34.91, lng: -56.21 } });
                options.container.append(button);
            }
            on(name, callback) { this.handlers[name] = callback; }
            once(name, callback) { if (name === 'load') setTimeout(callback, 0); }
            addControl() {} resize() {} remove() {} jumpTo() {}
        }
        class Marker {
            setLngLat(point) { this.point = point; return this; }
            addTo() { return this; } on() {} setDraggable() {} remove() {}
        }
        globalThis.mapboxgl = { Map, Marker, NavigationControl: class {} };
    });
    try {
        await page.goto(base + 'ingresar');
        await page.locator('#Email').fill(process.env.DESTINATION_EMAIL || 'commerce@example.test');
        await page.locator('#Password').fill(process.env.DESTINATION_PASSWORD || 'correct-password');
        await Promise.all([page.waitForNavigation(), page.getByRole('button', { name: 'Ingresar', exact: true }).click()]);
        const url = base + 'envios/' + (process.env.DESTINATION_SHIPMENT || '00000000-0000-0000-0000-000000000999') + '/destino';
        await page.goto(url);
        await page.getByRole('button', { name: 'Mapa simulado: seleccionar punto' }).waitFor();
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
        assert.equal(await page.getByText('Vista previa temporal:',{exact:false}).count(),0);
        assert.equal(await page.getByRole('button',{name:'Descartar vista previa temporal'}).count(),0);
        await page.locator('#destino-revisado').check();
        await page.setViewportSize({width:320,height:1000});
        await page.waitForFunction(() => !document.querySelector('.nr-destination-primary')?.disabled);
        assert.equal(await page.getByRole('button',{name:'Confirmar ubicación',exact:true}).isEnabled(),true);
        await page.screenshot({path:path.join(out,'selected-320.png'),fullPage:true});
        await page.getByRole('button',{name:'Confirmar ubicación',exact:true}).click();
        await page.getByText('Ubicación confirmada. El punto quedó listo para su planificación.',{exact:true}).waitFor();
        await page.reload();
        await page.getByRole('button',{name:'Mapa simulado: seleccionar punto'}).waitFor();
        await page.getByRole('heading',{name:'Primera 123',exact:true}).waitFor();
        await page.getByText('Punto registrado · pendiente de revisar.',{exact:true}).waitFor();
        await input.fill('Cambio');await page.getByRole('option',{name:'Cambio 123'}).click();
        await page.getByRole('button',{name:'Mapa simulado: seleccionar punto'}).click();
        await page.locator('#destino-revisado').check();
        await page.getByRole('button',{name:'Confirmar nueva ubicación',exact:true}).click();
        await page.getByText('Ubicación confirmada. El punto quedó listo para su planificación.',{exact:true}).waitFor();
        await page.reload();await page.getByRole('button',{name:'Mapa simulado: seleccionar punto'}).waitFor();
        await page.getByRole('heading',{name:'Cambio 123',exact:true}).waitFor();
        if(fullStack){
            await fs.writeFile(path.join(out,'conflict.ready'),'ready');
            for(let i=0;;i++){
                try{await fs.access(path.join(out,'conflict.applied'));break;}catch{}
                assert.ok(i<200,'fixture concurrency handshake timed out');await page.waitForTimeout(50);
            }
            await page.getByRole('button',{name:'Mapa simulado: seleccionar punto'}).click();
            await page.locator('#destino-revisado').check();
            await page.getByRole('button',{name:'Confirmar nueva ubicación',exact:true}).click();
            await page.getByRole('alert').filter({hasText:'El envío cambió mientras revisabas'}).waitFor();
            assert.equal(await page.locator('#destino-revisado').isChecked(),false);
            await page.waitForTimeout(500);
            await page.screenshot({path:path.join(out,'conflict-320.png'),fullPage:true});
            await page.locator('#destino-revisado').check();
            await page.getByRole('button',{name:'Confirmar nueva ubicación',exact:true}).click();
            await page.getByText('Ubicación confirmada. El punto quedó listo para su planificación.',{exact:true}).waitFor();
            await page.reload();await page.getByRole('button',{name:'Mapa simulado: seleccionar punto'}).waitFor();
            await page.getByRole('heading',{name:'Cambio 123',exact:true}).waitFor();
        }
        if (fullStack) {
            await input.fill('Fallo');
            await page.getByRole('alert').filter({ hasText: 'no está habilitada' }).waitFor();
        }
        await input.fill('Cuota');
        await page.getByRole('alert').filter({ hasText: 'Se agotó la cuota' }).waitFor();
        await page.getByRole('button', { name: 'Mapa simulado: seleccionar punto' }).click();
        await page.locator('#destino-revisado').check();
        await page.getByRole('button', { name: 'Confirmar nueva ubicación', exact: true }).click();
        await page.getByText('Ubicación confirmada. El punto quedó listo para su planificación.', { exact: true }).waitFor();
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
        console.log('PASS: short input, keyboard selection, address/coordinates save and reopen, adjusted marker/edit, real version conflict, manual fallback, quota/error/manual fallback, 1440/320px. Zero external requests.');
    } catch (error) {
        await page.screenshot({ path: path.join(out, 'failure.png'), fullPage: true });
        console.error((await page.locator('body').innerText()).slice(-4000));
        throw error;
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
