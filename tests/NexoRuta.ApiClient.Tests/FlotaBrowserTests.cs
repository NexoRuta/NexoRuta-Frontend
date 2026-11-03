using System.Diagnostics;
using System.Text.Json;

namespace NexoRuta.ApiClient.Tests;

public sealed partial class BackofficeSessionAndPagesTests
{
    [FleetBrowserFact]
    public async Task FleetBrowserUsesRealRazorAndIsolatedApiFixture()
    {
        object Vehicle(int index, string name, bool active, bool verified, string? state = null, bool returned = false, int cargo = 0)
        {
            var id = Guid.Parse($"00000000-0000-0000-0000-{index:000000000000}");
            return new
            {
                id, matricula = name, activo = active, version = 8,
                verificacionInicialEnUtc = verified ? (DateTimeOffset?)DateTimeOffset.UtcNow : null,
                cargaPendiente = cargo,
                ocupaciones = state is null ? Array.Empty<object>() : [new {
                    id, rutaId = RutaPrueba, repartidorAccesoId = TargetAccessId,
                    fecha = "2020-01-02", estadoRuta = state, version = 5, legada = false,
                    adquiridaEnUtc = DateTimeOffset.UtcNow,
                    retornoEnUtc = returned ? (DateTimeOffset?)DateTimeOffset.UtcNow : null,
                    cargaPendiente = cargo, puedeAnular = state == "Despachada" }],
                ultimaLiberacionEnUtc = (DateTimeOffset?)null, cargasIniciales = Array.Empty<object>()
            };
        }
        var api = new FakeApi { IncludeReparto = true, CanManageOperationalDecisions = true, FleetResponse = new[] {
            Vehicle(801, "B2-RETORNO", true, true, "Finalizada", cargo: 1),
            Vehicle(802, "B2-LIBERAR", true, true, "Finalizada", returned: true),
            Vehicle(803, "B2-DESCONOCIDO", true, false),
            Vehicle(804, "B2-INACTIVO", false, true),
            Vehicle(805, "B2-EN-CURSO", true, true, "EnCurso", cargo: 1),
            Vehicle(806, "B2-ANULAR", true, true, "Despachada"),
            Vehicle(807, "B2-DESCARGA", true, true, "Finalizada", returned: true, cargo: 1)
        } };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api);
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        var scratch = Path.Combine(Path.GetTempPath(), "nexoruta-fleet-browser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var script = Path.Combine(scratch, "fleet-probe.cjs");
        await File.WriteAllTextAsync(script, FleetBrowserProbe);
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(script);
        start.Environment["B2_BASE_URL"] = client.BaseAddress!.ToString();
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await stdout + "\n" + await stderr);
        // The browser exercised actual antiforgery/session/POST handling, not static HTML.
        var commands = api.RepartoPayloads.Where(c => c.Path.Contains("/api/reparto/flota/")).ToArray();
        Assert.Equal(2, commands.Length);
        Assert.All(commands, c => Assert.Equal(commands[0].Body.GetProperty("comandoId").GetGuid(), c.Body.GetProperty("comandoId").GetGuid()));
        Assert.All(commands, c => Assert.Equal(5, c.Body.GetProperty("version").GetInt64()));
        Assert.All(commands, c => Assert.Equal("Retorno físico inspeccionado", c.Body.GetProperty("motivo").GetString()));
    }

    private const string FleetBrowserProbe = """
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require(process.env.NEXORUTA_PLAYWRIGHT_MODULE);
(async () => {
    const base = process.env.B2_BASE_URL;
    assert.match(base, /^http:\/\/(localhost|127\.0\.0\.1):\d+\/$/);
    const output = process.env.NEXORUTA_B2_BROWSER_DIR;
    await fs.mkdir(output, { recursive: true });
    const browser = await chromium.launch({ headless: true });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.route('**/*', route => new URL(route.request().url()).origin === new URL(base).origin ? route.continue() : route.abort());
    try {
        await page.goto(base + 'ingresar');
        await page.locator('#Email').fill('user@example.test');
        await page.locator('#Password').fill('correct-password');
        await Promise.all([page.waitForURL(url => !url.pathname.includes('ingresar')), page.getByRole('button', { name: 'Ingresar', exact: true }).click()]);
        await page.goto(base + 'flota?fecha=2026-10-20');
        const cards = page.locator('[data-fleet-card]');
        assert.equal(await cards.count(), 7);
        assert.equal(await cards.locator('form').count(), 0);
        assert.equal(await page.locator('[data-fleet-count=total]').innerText(), '7');
        const occupied = cards.filter({ hasText: 'B2-EN-CURSO' });
        const opener = occupied.getByRole('button', { name: 'Ver operación', exact: true });
        await opener.focus(); await page.keyboard.press('Enter');
        const panel = page.locator('#flota-operacion');
        await panel.waitFor({ state: 'visible' });
        await page.waitForFunction(() => { const p = document.getElementById('flota-operacion'); return p.classList.contains('show') && !p.classList.contains('showing'); });
        await page.waitForFunction(() => document.getElementById('backoffice-app-shell').inert);
        assert.match(await panel.innerText(), /Recepción\/conciliación al resolver la ocupación/);
        assert.doesNotMatch(await panel.innerText(), /Falta recibir o conciliar 1 bulto/);
        assert.equal(await panel.locator('form').count(), 0);
        await page.keyboard.press('Shift+Tab');
        assert.equal(await panel.evaluate(el => el.contains(document.activeElement)), true);
        for (let i = 0; i < 18; i++) {
            await page.keyboard.press('Tab');
            assert.equal(await panel.evaluate(el => el.contains(document.activeElement)), true, 'Focus stays in operation panel');
        }
        for (const width of [1440, 768, 390, 320]) {
            await page.setViewportSize({ width, height: 1000 });
            if(width===320) { await page.emulateMedia({reducedMotion:'reduce'}); assert.equal(await panel.evaluate(el=>getComputedStyle(el).transitionDuration),'0s'); }
            assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
            if (width <= 390) assert.equal(Math.round(await panel.evaluate(el => el.getBoundingClientRect().width)), width);
            await page.screenshot({ path: path.join(output, `fleet-panel-${width}.png`) });
        }
        await page.keyboard.press('Escape');
        await page.waitForFunction(() => !document.getElementById('backoffice-app-shell').inert);
        assert.equal(await opener.evaluate(el => el === document.activeElement), true);
        await page.evaluate(() => {
            const cards = [...document.querySelectorAll('[data-fleet-card]')];
            cards.find(c=>c.dataset.name==='B2-DESCONOCIDO').querySelector('button').click();
            cards.find(c=>c.dataset.name==='B2-EN-CURSO').querySelector('button').click();
        });
        await page.waitForFunction(() => { const p=document.getElementById('flota-operacion'); return p.classList.contains('show') && !p.classList.contains('showing'); });
        assert.equal(await panel.locator('#fleet-panel-title').innerText(), 'B2-EN-CURSO');
        assert.equal(await panel.locator('form').count(), 0, 'Latest card replaces the initial inspection form');
        await page.keyboard.press('Escape');
        await page.waitForFunction(() => !document.getElementById('backoffice-app-shell').inert);
        assert.equal(await opener.evaluate(el=>el===document.activeElement), true);
        const initial = page.locator('[data-fleet-card]').filter({hasText:'B2-DESCONOCIDO'}).getByRole('button',{name:'Verificar disponibilidad',exact:true});
        await initial.click();
        await panel.getByRole('button',{name:'Cancelar acción',exact:true}).click();
        assert.equal(await panel.locator('form').count(),0);
        assert.equal(await panel.evaluate(el=>el.contains(document.activeElement)),true,'Cancelling an initial inspection retains focus inside panel');
        await page.keyboard.press('Escape');
        await page.waitForFunction(() => !document.getElementById('backoffice-app-shell').inert);
        assert.equal(await initial.evaluate(el=>el===document.activeElement),true);
        const search = page.locator('#fleet-search'); const filter = page.locator('#fleet-filter');
        await search.fill('B2-RETORNO');
        assert.equal(await page.locator('[data-fleet-card]:visible').count(), 1);
        assert.equal(await page.locator('[data-fleet-count=total]').innerText(), '7');
        await filter.selectOption('Disponible');
        assert.equal(await page.locator('[data-fleet-card]:visible').count(), 0);
        assert.equal(await page.locator('[data-fleet-empty]').isVisible(), true);
        await page.evaluate(() => { history.pushState(null, '', '/flota?buscarVehiculo=B2-ANULAR&disponibilidad=Ocupado'); dispatchEvent(new PopStateEvent('popstate')); });
        assert.equal(await page.locator('[data-fleet-card]:visible').count(), 1);
        assert.equal(await search.inputValue(), 'B2-ANULAR');
        await page.goto(base + 'flota?fecha=2027-02-03');
        await page.locator('#fleet-search').fill('B2-RETORNO');
        await page.locator('#fleet-filter').selectOption('Ocupado');
        const returning = page.locator('[data-fleet-card]').filter({ hasText: 'B2-RETORNO' });
        assert.equal(await returning.isVisible(), true, 'Occupations are not date-filtered');
        await returning.getByRole('button', { name: 'Ver operación', exact: true }).click();
        await panel.getByRole('button', { name: 'Confirmar retorno físico (sin descarga)', exact: true }).click();
        const form = panel.locator('form');
        assert.equal(await form.count(), 1);
        const command = await form.locator('[name=FlotaComando]').inputValue();
        const occupation = await form.locator('[name=FlotaOcupacion]').inputValue();
        await form.locator('textarea').fill('Retorno físico inspeccionado');
        await form.getByRole('checkbox').check();
        await form.evaluate(el => el.addEventListener('submit', () => {
            const duplicate = new Event('submit', { bubbles: true, cancelable: true });
            if (el.dataset.duplicateProbe) return;
            el.dataset.duplicateProbe = 'true';
            el.dispatchEvent(duplicate);
            sessionStorage.setItem('fleet-duplicate-prevented', String(duplicate.defaultPrevented));
        }));
        await Promise.all([page.waitForNavigation(), form.getByRole('button', { name: 'Confirmar retorno físico (sin descarga)', exact: true }).click()]);
        await panel.waitFor({ state: 'visible' });
        await page.waitForFunction(() => { const p = document.getElementById('flota-operacion'); return p.classList.contains('show') && !p.classList.contains('showing'); });
        const retry = panel.locator('form');
        assert.equal(await page.evaluate(() => sessionStorage.getItem('fleet-duplicate-prevented')), 'true');
        assert.equal(await retry.locator('[name=FlotaComando]').inputValue(), command);
        assert.equal(await retry.locator('[name=FlotaOcupacion]').inputValue(), occupation);
        assert.equal(await retry.locator('[name=FlotaVersion]').inputValue(), '5');
        assert.equal(await retry.locator('[name=BuscarVehiculo]').inputValue(), 'B2-RETORNO');
        assert.equal(await retry.locator('[name=Disponibilidad]').inputValue(), 'Ocupado');
        assert.equal(new URL(await page.locator('[data-fleet-refresh]').getAttribute('href'), base).searchParams.has('handler'), false);
        assert.equal(await retry.locator('textarea').inputValue(), 'Retorno físico inspeccionado');
        await Promise.all([page.waitForNavigation(), retry.getByRole('button', { name: 'Reenviar la misma confirmación', exact: true }).click()]);
        await panel.waitFor({ state: 'visible' });
        await page.waitForFunction(() => { const p = document.getElementById('flota-operacion'); return p.classList.contains('show') && !p.classList.contains('showing'); });
        assert.equal(await panel.locator('[name=FlotaComando]').inputValue(), command);
        assert.deepEqual(errors, []);
        await fs.writeFile(path.join(output, 'fleet-fixture-result.json'), JSON.stringify({ isolated: true, checks: ['cards', 'global-counts', 'filters', 'popstate', 'all-dates', 'normal-custody', 'focus-trap', 'escape-restore', 'rapid-latest-selection', 'reduced-motion', 'four-widths', 'single-submit', 'exact-retry'], errors }, null, 2));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
""";

    private sealed class FleetBrowserFactAttribute : FactAttribute
    {
        public FleetBrowserFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NEXORUTA_B2_BROWSER_DIR")))
                Skip = "Opt-in real-browser check: set NEXORUTA_B2_BROWSER_DIR and NEXORUTA_PLAYWRIGHT_MODULE. No operational API/database is used.";
        }
    }
}
