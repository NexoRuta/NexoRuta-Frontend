using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.ZonasCobertura.Clients;
using NexoRuta.Backoffice.Features.ZonasCobertura.Pages;

namespace NexoRuta.ApiClient.Tests;

public sealed class ZonasCoberturaPagesTests
{
    [Fact]
    public async Task OverviewIncludesInactiveGeometryWhileListKeepsActiveFilter()
    {
        var active = Guid.NewGuid(); var inactive = Guid.NewGuid();
        using var http = new HttpClient(new Handler(active, inactive)) { BaseAddress = new Uri("https://api.test/") };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Mapbox:PublicAccessToken"] = "pk.synthetic" }).Build();
        var page = new IndexModel(new ZonasCoberturaApiClient(http), config)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(SesionUsuario.ClaimApiToken, "synthetic")], "test"))
            } }
        };
        await page.OnGetAsync();
        Assert.Null(page.Error); Assert.Null(page.ErrorMapa);
        Assert.Equal(active, Assert.Single(page.Activas).Id);
        Assert.Empty(page.Inactivas);
        using var map = JsonDocument.Parse(page.MapaJson!);
        Assert.Equal(new[] { "Activa", "Inactiva" }, map.RootElement.EnumerateArray()
            .Select(z => z.GetProperty("estado").GetString()).Order().ToArray());
    }

    private sealed class Handler(Guid active, Guid inactive) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var isList = request.RequestUri!.AbsolutePath.EndsWith("/zonas");
            var isInactive = request.RequestUri.Query.Contains("estado=Inactiva");
            var id = isList ? (isInactive ? inactive : active)
                : Guid.Parse(request.RequestUri.Segments.Last());
            var state = id == inactive ? "Inactiva" : "Activa";
            object body = isList
                ? new { items = new[] { new { id, nombre = state, estado = state } }, pagina = 1, tamanoPagina = 50, total = 1 }
                : new { id, nombre = state, estado = state,
                    geometria = new { type = "MultiPolygon", coordinates = Array.Empty<object>() }, franjas = Array.Empty<object>() };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        }
    }
}
