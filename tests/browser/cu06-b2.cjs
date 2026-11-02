// Runs against the Kestrel Razor fixture only. Never point this at an operational host.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require(process.env.NEXORUTA_PLAYWRIGHT_MODULE);

(async () => {
    const base = process.env.B2_BASE_URL;
    assert.match(base, /^http:\/\/(localhost|127\.0\.0\.1):\d+\/$/);
    const out = process.env.NEXORUTA_B2_BROWSER_DIR;
    await fs.mkdir(out, { recursive: true });
    const browser = await chromium.launch({ headless: true });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    let posts = 0;
    // No external resources or APIs can receive fixture data.
    await page.route('**/*', async route => {
        const request = route.request(), url = new URL(request.url());
        if (url.origin !== new URL(base).origin) return route.abort();
        if (request.method() === 'POST' && url.pathname === '/rutas') {
            posts++;
        }
        return route.continue();
    });
    const results = { browser: browser.version(), isolated: true, viewports: [], checks: [], pageErrors: [], failedResources: [] };
    page.on('pageerror', error => results.pageErrors.push(error.message));
    page.on('response', response => { if (response.status() >= 400) results.failedResources.push({ url: new URL(response.url()).pathname, status: response.status() }); });
    results.postUrls = [];
    page.on('request', request => { if (request.method() === 'POST') results.postUrls.push(new URL(request.url()).pathname + new URL(request.url()).search); });
    results.events = [];
    await page.exposeFunction('recordFleetEvent', entry => results.events.push(entry));
    const check = name => results.checks.push(name);
    try {
        await page.goto(base + 'ingresar');
        await page.locator('#Email').fill('user@example.test');
        await page.locator('#Password').fill('correct-password');
        await Promise.all([page.waitForURL(url => !url.pathname.includes('ingresar')), page.getByRole('button', { name: 'Ingresar', exact: true }).click()]);
        const routes = base + 'rutas?fecha=2026-10-20';
        await page.goto(routes);
        const fleet = page.locator('section[aria-labelledby="flota-titulo"]');
        await fleet.getByText('B2-RETORNO', { exact: true }).waitFor();
        assert.equal(await page.evaluate(() => typeof initializeSingleSubmitForms), 'function', 'Existing single-submit JavaScript must actually load');
        const release = fleet.locator('details.mb-3').filter({ hasText: 'B2-LIBERAR' });
        await release.getByText('Confirmar disponibilidad y liberar', { exact: true }).first().click();
        await release.locator('form').screenshot({ path: path.join(out, 'liberacion-escritorio.png') });
        for (const width of [1440, 390, 320]) {
            await page.setViewportSize({ width, height: 1000 });
            await page.evaluate(() => scrollTo(0, 0));
            await page.screenshot({ path: path.join(out, `rutas-${width}.png`), fullPage: true });
            await page.screenshot({ path: path.join(out, `rutas-${width}-viewport.png`) });
            const dimensions = await page.evaluate(() => ({ viewport: innerWidth, content: document.documentElement.scrollWidth }));
            results.viewports.push({ width, ...dimensions });
            assert.ok(dimensions.content <= width, `Horizontal overflow at ${width}: ${dimensions.content}`);
        }
        check('No horizontal page overflow at desktop, 390 and 320 CSS px');
        await release.locator('form').screenshot({ path: path.join(out, 'liberacion-320.png') });
        assert.match(await fleet.locator('details.mb-3').filter({ hasText: 'B2-DESCONOCIDO' }).innerText(), /Disponibilidad desconocida/);
        assert.match(await fleet.locator('details.mb-3').filter({ hasText: 'B2-INACTIVO' }).innerText(), /no habilitado para despachar/);
        assert.match(await fleet.locator('details.mb-3').filter({ hasText: 'B2-EN-CURSO' }).innerText(), /Falta cerrar la jornada/);
        assert.match(await fleet.locator('details.mb-3').filter({ hasText: 'B2-DESCARGA' }).innerText(), /Falta recibir o conciliar 1 bulto/);
        check('Unknown, inactive, occupied, returned-with-cargo and ready-for-release states have distinct explanations');
        await page.goto(base + 'rutas?fecha=2027-02-03');
        assert.equal(await fleet.getByText('B2-RETORNO', { exact: true }).isVisible(), true);
        assert.ok(await fleet.locator('a[href*="fecha=2020-01-02"]').count() > 0);
        check('Previous-date occupations and responsible route links remain visible with another daily filter');
        await page.setViewportSize({ width: 320, height: 800 });
        const vehicle = fleet.locator('details.mb-3').filter({ hasText: 'B2-RETORNO' });
        const action = vehicle.getByText('Confirmar retorno físico (sin descarga)', { exact: true }).first();
        await action.focus();
        assert.equal(await action.evaluate(el => el === document.activeElement), true);
        await page.keyboard.press('Enter');
        const reason = vehicle.getByLabel('Motivo y comprobación realizada');
        await page.keyboard.press('Tab');
        assert.equal(await reason.evaluate(el => el === document.activeElement), true);
        const focus = await reason.evaluate(el => ({ outline: getComputedStyle(el).outlineStyle, shadow: getComputedStyle(el).boxShadow }));
        assert.ok(focus.outline !== 'none' || focus.shadow !== 'none', 'Reason field needs a visible focus indicator');
        await reason.fill('Constatación simulada en fixture aislada, no acredita retorno real.');
        await reason.scrollIntoViewIfNeeded();
        await page.screenshot({ path: path.join(out, 'confirmacion-320.png'), fullPage: true });
        await page.screenshot({ path: path.join(out, 'confirmacion-320-viewport.png') });
        check('Keyboard opens action, Tab reaches associated textarea, focus indicator is present');
        const form = vehicle.locator('form').first();
        const command = await form.locator('[name="FlotaComando"]').inputValue();
        const occupation = await form.locator('[name="FlotaOcupacion"]').inputValue();
        // Inspect submit state synchronously, before navigation replaces the document.
        const button = form.locator('button[type="submit"]');
        results.formBeforeSubmit = await form.evaluate(el => ({ valid: el.checkValidity(), single: el.hasAttribute('data-single-submit'), invalid: [...el.elements].filter(x => !x.validity.valid).map(x => x.name), motive: el.querySelector('textarea').value }));
        await form.evaluate(el => {
            let first = true;
            el.addEventListener('submit', event => {
                if (!first) {
                    window.recordFleetEvent({ type: 'duplicate-submit', prevented: event.defaultPrevented });
                    return;
                }
                first = false;
                window.recordFleetEvent({ type: 'first-submit', disabled: el.querySelector('button[type="submit"]').disabled, busy: el.getAttribute('aria-busy') });
                el.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
            });
        });
        await page.keyboard.press('Tab');
        assert.equal(await button.evaluate(el => el === document.activeElement), true);
        const navigated = page.waitForNavigation();
        await Promise.all([navigated, page.keyboard.press('Enter')]);
        assert.deepEqual(results.events.find(e => e.type === 'first-submit'), { type: 'first-submit', disabled: true, busy: 'true' });
        assert.deepEqual(results.events.find(e => e.type === 'duplicate-submit'), { type: 'duplicate-submit', prevented: true });
        assert.equal(posts, 1);
        const retry = page.locator('form:has(textarea[name="FlotaMotivo"][readonly])');
        assert.equal(await retry.locator('textarea').inputValue(), 'Constatación simulada en fixture aislada, no acredita retorno real.');
        assert.equal(await retry.locator('[name="FlotaComando"]').inputValue(), command);
        assert.equal(await retry.locator('[name="FlotaOcupacion"]').inputValue(), occupation);
        assert.equal(await retry.locator('[name="FlotaVersion"]').inputValue(), '5');
        assert.match(await fleet.getByRole('alert').innerText(), /ocupación camb/);
        await page.screenshot({ path: path.join(out, 'conflicto-320.png'), fullPage: true });
        await page.screenshot({ path: path.join(out, 'conflicto-320-viewport.png') });
        check('Single-submit prevents second POST; 409 preserves exact reason, command, occupation and version');
    } finally {
        await fs.writeFile(path.join(out, 'browser-results.json'), JSON.stringify(results, null, 2));
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
