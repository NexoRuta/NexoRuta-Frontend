using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using NexoRuta.ApiClient.Features.Operadores.Clients;

namespace NexoRuta.ApiClient.Tests;

public sealed class OperadoresApiClientTests
{
    [Fact]
    public async Task AlternativesUseExactInvariantPointExclusionAndBearerToken()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-UY");
        try
        {
            var id = Guid.NewGuid();
            using var handler = new AlternativesHandler((request, ct) =>
            {
                Assert.Equal($"http://api.test/api/operadores/alternativas-envio?latitud=-34.900000000000006&longitud=-56.23456789012345&excluirOperadorId={id:D}",request.RequestUri!.AbsoluteUri);
                Assert.Equal("Bearer",request.Headers.Authorization!.Scheme);
                Assert.Equal("private-test-token",request.Headers.Authorization.Parameter);
                Assert.True(ct.CanBeCanceled);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=JsonContent.Create(new[] { new { operadorId=id,nombre="Operator",modalidades=new[]{"Urgente"} } }) });
            });
            using var http = new HttpClient(handler) { BaseAddress=new Uri("http://api.test/") };
            using var cancellation = new CancellationTokenSource();
            var result=await new OperadoresApiClient(http).AlternativasEnvioAsync(-34.900000000000006,-56.23456789012345,id,"private-test-token",cancellation.Token);
            Assert.Equal("Urgente",Assert.Single(Assert.Single(result).Modalidades));
        }
        finally { CultureInfo.CurrentCulture=previous; }
    }

    [Fact]
    public async Task AlternativeRequestHonorsCancellation()
    {
        using var handler = new AlternativesHandler(async (_, ct) => { await Task.Delay(Timeout.Infinite,ct); return new(HttpStatusCode.OK); });
        using var http = new HttpClient(handler) { BaseAddress=new Uri("http://api.test/") };
        using var cancellation = new CancellationTokenSource();
        var request = new OperadoresApiClient(http).AlternativasEnvioAsync(-34.9,-56.2,Guid.NewGuid(),"token",cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }

    private sealed class AlternativesHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) => respond(request,ct);
    }
}
