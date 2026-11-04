using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.Commerce.Core.Security;
using NexoRuta.Commerce.Pages.Envios;

namespace NexoRuta.ApiClient.Tests;

public sealed class EtiquetasEnvioTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(404)]
    [InlineData(403)]
    [InlineData(503)]
    public async Task DownloadUsesBearerNotUrlAndPreservesFailureStatus(int status)
    {
        var id = Guid.NewGuid();
        using var http = Client(id, status);
        var client = new EnviosApiClient(http);
        if (status == 200) Assert.Equal("%PDF-fixture"u8.ToArray(), await client.DescargarEtiquetasAsync(id, "controlled-token"));
        else Assert.Equal((HttpStatusCode)status, (await Assert.ThrowsAsync<NexoRutaApiException>(() => client.DescargarEtiquetasAsync(id, "controlled-token"))).StatusCode);
    }

    [Fact]
    public async Task CommerceDownloadRequiresReadPolicyAndReturnsAttachmentWithoutCache()
    {
        var id = Guid.NewGuid();
        using var http = Client(id, 200);
        var page = new EtiquetasModel(new EnviosApiClient(http))
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() }
        };
        page.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(SesionUsuario.ClaimApiToken, "controlled-token")], "test"));
        var result = Assert.IsType<FileContentResult>(await page.OnGetAsync(id, CancellationToken.None));
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal($"etiquetas-{id:D}.pdf", result.FileDownloadName);
        Assert.Equal("no-store", page.Response.Headers.CacheControl);
        Assert.Contains(typeof(EtiquetasModel).GetCustomAttributes(false).OfType<AuthorizeAttribute>(), a => a.Policy == ApiPermissionAuthorizationHandler.ShipmentsReadPolicy);
    }

    private static HttpClient Client(Guid id, int status) => new(new DownloadHandler(id, status)) { BaseAddress = new Uri("http://controlled.test/") };
    private sealed class DownloadHandler(Guid id, int status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/api/envios/{id:D}/etiquetas", request.RequestUri!.PathAndQuery);
            Assert.Equal("controlled-token", request.Headers.Authorization!.Parameter);
            HttpContent content = status == 200 ? new ByteArrayContent("%PDF-fixture"u8.ToArray()) : new StringContent("{\"message\":\"Controlled failure\"}");
            content.Headers.ContentType = new MediaTypeHeaderValue(status == 200 ? "application/pdf" : "application/json");
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = content });
        }
    }
}
