using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.Backoffice.Pages.Envios;

namespace NexoRuta.ApiClient.Tests;

public sealed class EtiquetasOperadorTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(503)]
    public async Task OperatorProxyUsesScopedBearerReadPreservesFailuresAndNeverCaches(int status)
    {
        var id=Guid.NewGuid();
        using var http = new HttpClient(new LabelHandler(id,status)) {BaseAddress=new Uri("http://controlled.test/")};
        var page = new EtiquetasModel(new EnviosApiClient(http)) {PageContext=new PageContext {HttpContext=new DefaultHttpContext()}};
        page.HttpContext.User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(SesionUsuario.ClaimApiToken,"controlled-token")],"test"));
        var result=await page.OnGetAsync(id,CancellationToken.None);
        Assert.Equal("no-store",page.Response.Headers.CacheControl);
        Assert.Contains(typeof(EtiquetasModel).GetCustomAttributes(false).OfType<AuthorizeAttribute>(),a=>a.Policy=="envios.consultar");
        if(status==200)
        {
            var pdf=Assert.IsType<FileContentResult>(result);
            Assert.Equal("application/pdf",pdf.ContentType);
            Assert.Equal($"etiquetas-{id:D}.pdf",pdf.FileDownloadName);
            Assert.Equal("%PDF-controlled"u8.ToArray(),pdf.FileContents);
        }
        else Assert.Equal(status,Assert.IsType<ObjectResult>(result).StatusCode);
    }

    private sealed class LabelHandler(Guid id,int status):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Get,request.Method);
            Assert.Equal($"/api/envios/{id:D}/etiquetas",request.RequestUri!.PathAndQuery);
            Assert.Equal("controlled-token",request.Headers.Authorization!.Parameter);
            HttpContent content=status==200?new ByteArrayContent("%PDF-controlled"u8.ToArray()):new StringContent("{\"message\":\"controlled failure\"}");
            content.Headers.ContentType=new MediaTypeHeaderValue(status==200?"application/pdf":"application/json");
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status){Content=content});
        }
    }
}
