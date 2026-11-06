using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.Backoffice.Pages.Reprogramaciones;

namespace NexoRuta.ApiClient.Tests;

public sealed class RescheduleDecisionPageTests
{
    [Theory]
    [InlineData(true, "Rechazada", "rechazada")]
    [InlineData(false, "Tratada", "aprobada")]
    public async Task ConcurrentDecisionDisplaysPersistedOutcomeInsteadOfTheRequestedAction(bool approve, string persisted, string expected)
    {
        var actor = new ClaimsPrincipal(new ClaimsIdentity([new Claim(SesionUsuario.ClaimApiToken,"synthetic-token")],"fixture"));
        var page = new IndexModel(new Clients(persisted))
        {
            SolicitudId = Guid.NewGuid(), VersionEnvio = 2, Aprobar = approve, Motivo = "Decisión operativa sintética",
            PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = actor } }
        };
        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync());
        Assert.Contains(expected, page.Mensaje, StringComparison.OrdinalIgnoreCase);
    }
    private sealed class Clients(string persisted) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(persisted)) { BaseAddress = new Uri("http://controlled.test/") };
    }
    private sealed class Handler(string persisted) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("synthetic-token", request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { estado = persisted }) });
        }
    }
}
