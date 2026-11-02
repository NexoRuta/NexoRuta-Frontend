using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace NexoRuta.ApiClient.Tests;

public sealed partial class BackofficeSessionAndPagesTests
{
    [DestinationBrowserFact]
    public async Task DestinationSearchSelectSaveEditAndManualFallbackInChromium()
    {
        var api = new FakeApi();
        api.PermissionOverrides["jwt-commerce"] = ["envios.crear", "envios.consultar"];
        var state = new DestinationState();
        using var factory = new DestinationFactory(api, state);
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/browser/destination-geocoding.cjs")));
        start.Environment["DESTINATION_BASE_URL"] = client.BaseAddress!.ToString();
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await output + "\n" + await error);
        Assert.Equal(new[] { "Primera", "Cambio", "Cuota", "Fallo" }, state.Searches.ToArray());
        Assert.Equal(3, state.Version);
        Assert.Equal((-34.91, -56.21), (state.Lat, state.Lon));
        Assert.Equal("Cambio 123",state.Address);
    }

    private sealed class DestinationBrowserFactAttribute : FactAttribute
    {
        public DestinationBrowserFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NEXORUTA_DESTINATION_BROWSER_DIR")))
                Skip = "Opt-in Chromium with fake API and map SDK; external requests blocked.";
        }
    }

    private sealed class DestinationFactory(FakeApi api, DestinationState state) : WebApplicationFactory<NexoRuta.Commerce.Components.App>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
                { ["Mapbox:PublicAccessToken"] = "pk.synthetic-not-a-credential", ["OpenTelemetry:Enabled"] = "false" }));
            builder.ConfigureTestServices(services => services.Configure<HttpClientFactoryOptions>("NexoRuta", options =>
                options.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = new DestinationHandler(state, api.CreateHandler()))));
        }
    }

    private sealed class DestinationState
    {
        public ConcurrentQueue<string> Searches { get; } = new();
        public long Version;
        public double? Lat, Lon;
        public string Address = "Registrada 123";
    }

    private sealed class DestinationHandler(DestinationState state, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (!path.StartsWith("/api/envios/", StringComparison.Ordinal) || !path.Contains("/destino"))
                return await base.SendAsync(request, ct);
            Assert.Equal("jwt-commerce", request.Headers.Authorization?.Parameter);
            var id = Guid.Parse(path.Split('/')[3]);
            if (path.EndsWith("/busqueda"))
            {
                var query = (await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("consulta").GetString()!;
                state.Searches.Enqueue(query);
                if (query == "Cuota") return Json(HttpStatusCode.TooManyRequests, new { detail = "Se agotó la cuota de búsqueda de direcciones." });
                if (query == "Fallo") return Json(HttpStatusCode.ServiceUnavailable, new { detail = "Servicio deshabilitado." });
                return Json(HttpStatusCode.OK, new { envioId = id, direccion = state.Address, version = state.Version,
                    atribucion = "© Mapbox — temporal",
                    propuestas = new[] { new { descripcion = query + " 123", latitud = -34.9, longitud = -56.2 } } });
            }
            if (request.Method == HttpMethod.Put)
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                Assert.Equal(state.Version, body.GetProperty("version").GetInt64());
                state.Address = body.GetProperty("direccion").GetString()!;
                state.Lat = body.GetProperty("latitud").GetDouble(); state.Lon = body.GetProperty("longitud").GetDouble(); state.Version++;
                return new(HttpStatusCode.NoContent);
            }
            return Json(HttpStatusCode.OK, new { envioId = id, destinatarioNombre = "Persona sintética", direccion = state.Address,
                version = state.Version, estado = "Admitido", latitud = state.Lat, longitud = state.Lon,
                confirmadaEnUtc = state.Version == 0 ? (DateTimeOffset?)null : DateTimeOffset.Parse("2026-11-01T12:00:00Z") });
        }
        private static HttpResponseMessage Json(HttpStatusCode status, object value) => new(status) { Content = JsonContent.Create(value) };
    }
}
