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
        var script = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/browser/cu06-b2.cjs"));
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
        Assert.Single(api.RepartoPayloads, c => c.Path.Contains("/api/reparto/flota/"));
    }

    private sealed class FleetBrowserFactAttribute : FactAttribute
    {
        public FleetBrowserFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NEXORUTA_B2_BROWSER_DIR")))
                Skip = "Opt-in real-browser check: set NEXORUTA_B2_BROWSER_DIR and NEXORUTA_PLAYWRIGHT_MODULE. No operational API/database is used.";
        }
    }
}
