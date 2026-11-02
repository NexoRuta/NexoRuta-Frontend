// Real Razor/Kestrel and in-memory API only; external traffic is blocked.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require(process.env.NEXORUTA_PLAYWRIGHT_MODULE);
(async () => {
    const base = process.env.B3_BASE_URL, out = process.env.NEXORUTA_B3_BROWSER_DIR;
    assert.match(base, /^http:\/\/(localhost|127\.0\.0\.1):\d+\/$/);
    await fs.mkdir(out, { recursive: true });
    const browser = await chromium.launch({ headless: true });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    const evidence = { browser: browser.version(), isolated: true, checks: [], screenshots: [], errors: [] };
    page.on('pageerror', e => evidence.errors.push(e.message));
    await page.route('**/*', r => new URL(r.request().url()).origin === new URL(base).origin ? r.continue() : r.abort());
    const submit = async button => { await Promise.all([page.waitForNavigation(), button.click()]); };
    try {
        await page.goto(base + 'ingresar');
        await page.locator('#Email').fill('user@example.test');
        await page.locator('#Password').fill('correct-password');
        await submit(page.getByRole('button', { name: 'Ingresar', exact: true }));
        await page.goto(base + 'configuracion-reparto');
        const section = page.locator('section[aria-labelledby="base-heading"]');
        await section.getByText('Base no configurada', { exact: true }).waitFor();
        await page.getByLabel('Dirección de la base', { exact: true }).fill('Base controlada A');
        await page.getByLabel('Servicio por parada (minutos)', { exact: true }).fill('12');
        await page.getByLabel('Margen de retorno (minutos)', { exact: true }).fill('7');
        await submit(section.getByRole('button', { name: 'Guardar base y parámetros', exact: true }));
        await section.getByText('Punto pendiente de confirmación', { exact: true }).waitFor();
        await submit(section.getByRole('button', { name: 'Proponer punto para la dirección guardada' }));
        await section.getByText('Punto pendiente de confirmación', { exact: true }).waitFor();
        assert.equal(await section.locator('input[name="BaseLatitud"][type="number"]').inputValue(), '');
        evidence.checks.push('Saved address and explicit minutes; geocoder proposal does not confirm or fill point');
        await submit(section.getByRole('button', { name: 'Confirmar este punto: Punto de prueba' }));
        await section.getByText(/Punto confirmado en/).waitFor();
        assert.equal(await page.getByLabel('Latitud de salida y retorno', { exact: true }).inputValue(), '-34.9');
        evidence.checks.push('Explicit point confirmation and server actor/time visible');
        for (const width of [1440, 390, 320]) {
            await page.setViewportSize({ width, height: 1000 });
            assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `Overflow at ${width}`);
            const file = `base-${width}.png`; await section.screenshot({ path: path.join(out, file) }); evidence.screenshots.push(file);
        }
        await page.getByLabel('Dirección de la base', { exact: true }).focus();
        assert.equal(await page.getByLabel('Dirección de la base', { exact: true }).evaluate(e => e === document.activeElement), true);
        await page.keyboard.press('Tab');
        assert.equal(await page.getByLabel('Servicio por parada (minutos)', { exact: true }).evaluate(e => e === document.activeElement), true);
        // Conflict through a genuinely stale version. Do not retry with a new token.
        await page.locator('input[name="BaseVersion"]').evaluate(e => e.value = '0');
        await page.getByLabel('Dirección de la base', { exact: true }).fill('Conservar tras conflicto');
        await submit(section.getByRole('button', { name: 'Guardar base y parámetros', exact: true }));
        await section.getByRole('alert').waitFor();
        assert.equal(await page.getByLabel('Dirección de la base', { exact: true }).inputValue(), 'Conservar tras conflicto');
        assert.equal(await page.locator('input[name="BaseVersion"]').inputValue(), '0');
        await section.screenshot({ path: path.join(out, 'conflicto-320.png') });
        evidence.checks.push('Keyboard labels/order, 320px, stale conflict retains entered address/minutes/version without retry');
        await page.goto(base + 'configuracion-reparto');
        await page.getByLabel('Dirección de la base', { exact: true }).fill('Base controlada B');
        await submit(section.getByRole('button', { name: 'Guardar base y parámetros', exact: true }));
        await section.getByText('Punto pendiente de confirmación', { exact: true }).waitFor();
        assert.equal(await page.getByLabel('Latitud de salida y retorno', { exact: true }).inputValue(), '');
        evidence.checks.push('Address edit invalidates the confirmed point');
        await page.goto(base + 'rutas/planificar?fecha=2026-10-20');
        await page.getByLabel('Salida planificada (fecha y hora local)', { exact: true }).fill('2026-10-20T23:00');
        await page.locator('[data-plan-check]').first().check();
        await page.locator('[data-stop-date]').first().fill('2026-10-21');
        await page.locator('[data-stop-time]').first().fill('00:15');
        await page.locator('[data-stop-service]').first().fill('20');
        await page.locator('[data-stop-order]').first().fill('1');
        await page.locator('[data-stop-date]').first().dispatchEvent('input');
        assert.match(await page.locator('[data-summary-time]').innerText(), /2026-10-21.*00:15/);
        for (const width of [1440, 390, 320]) {
            await page.setViewportSize({ width, height: 1000 });
            assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `Plan overflow at ${width}`);
            assert.ok(await page.locator(".nr-stop-fields").first().evaluate(el => el.scrollWidth <= el.clientWidth), `Stop fields overflow at ${width}`);
            await page.locator("h1").click();
            await page.screenshot({ path: path.join(out, `plan-${width}.png`), fullPage: true });
        }
        evidence.checks.push('Full next-day manual arrival, service override and planned departure visible; JS summary updates without road ETA');
        await page.goto(base + 'rutas?fecha=2026-10-20');
        await page.screenshot({ path: path.join(out, 'rutas-320.png'), fullPage: true });
        evidence.checks.push('Routes remain available alongside the isolated configuration workflow');
        assert.deepEqual(evidence.errors, []);
    } finally {
        await fs.writeFile(path.join(out, 'resultado.json'), JSON.stringify(evidence, null, 2));
        await browser.close();
    }
})().catch(e => { console.error(e); process.exitCode = 1; });
