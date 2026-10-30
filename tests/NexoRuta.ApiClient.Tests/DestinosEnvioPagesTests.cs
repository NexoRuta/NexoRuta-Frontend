using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.ApiClient.Features.Envios.Contracts.Responses;
using NexoRuta.Commerce.Components.Features.Envios.Shared;

namespace NexoRuta.ApiClient.Tests;

public sealed class DestinosEnvioPagesTests
{
    private static readonly Guid EnvioId = Guid.NewGuid();
    private const string ApiToken = "commerce-session";

    [Theory]
    [InlineData(200, null)]
    [InlineData(401, "Mapbox rechazó la búsqueda. Revisá la configuración del servicio.")]
    [InlineData(429, "La búsqueda alcanzó el límite temporal. Esperá un momento y volvé a intentar.")]
    [InlineData(503, "Mapbox no pudo completar la búsqueda. Volvé a intentarlo.")]
    public async Task BusquedaDistingueSinResultadosDeErroresDelProveedor(int status, string? expected)
    {
        var picker = new SelectorDestino();
        var type = typeof(SelectorDestino);
        type.GetProperty("Editable", BindingFlags.Public | BindingFlags.Instance)!.SetValue(picker, true);
        type.GetField("token", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(picker, "pk-test-public");
        type.GetField("modulo", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(picker, new ModuloGeocodificacion(status));

        await (Task)type.GetMethod("CambiarBusqueda", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(picker, [new ChangeEventArgs { Value = "Montevideo" }])!;

        var message = type.GetField("mensajeBusqueda", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(picker) as string;
        var noResults = (bool)type.GetProperty("sinResultados", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(picker)!;
        if (expected is null)
        {
            Assert.Null(message);
            Assert.True(noResults);
        }
        else
        {
            Assert.Equal(expected, message);
            Assert.False(noResults);
        }
    }

    [Fact]
    public async Task ClienteConservaContratoDeLecturaYConfirmacionConVersion()
    {
        var requests = new List<(HttpMethod Method, string Path, string? Authorization, string? Body)>();
        using var http = new HttpClient(new Handler(request =>
        {
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            requests.Add((request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.Parameter, body));
            if (request.Method == HttpMethod.Get)
                return Respuesta(200, JsonSerializer.Serialize(new DestinoEnvioResponse(EnvioId, "Destinatario", "Calle 123", 7,
                    "EnDeposito", null, null, null)));
            return Respuesta(204, "");
        })) { BaseAddress = new Uri("http://api.test/") };
        var client = new DestinosEnvioApiClient(http);

        var current = await client.ObtenerAsync(EnvioId, ApiToken);
        await client.ConfirmarAsync(EnvioId, current.Version, -34.9, -56.2, ApiToken);

        Assert.Equal("Destinatario", current.DestinatarioNombre);
        Assert.Equal(2, requests.Count);
        Assert.All(requests, request => Assert.Equal(ApiToken, request.Authorization));
        Assert.Equal($"/api/envios/{EnvioId}/destino", requests[0].Path);
        Assert.Equal(HttpMethod.Put, requests[1].Method);
        Assert.Equal($"/api/envios/{EnvioId}/destino", requests[1].Path);
        using var body = JsonDocument.Parse(requests[1].Body!);
        Assert.Equal(7, body.RootElement.GetProperty("version").GetInt64());
        Assert.Equal(-34.9, body.RootElement.GetProperty("latitud").GetDouble());
        Assert.Equal(-56.2, body.RootElement.GetProperty("longitud").GetDouble());
        Assert.Equal(3, body.RootElement.EnumerateObject().Count());
    }

    private static HttpResponseMessage Respuesta(int status, string body) => new((HttpStatusCode)status)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class ModuloGeocodificacion(int status) : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (typeof(TValue).FullName == "Microsoft.JSInterop.Infrastructure.IJSVoidResult")
                return ValueTask.FromResult(default(TValue)!);
            var type = typeof(SelectorDestino).GetNestedType("RespuestaGeocodificacion", BindingFlags.NonPublic)!;
            var response = Activator.CreateInstance(type, nonPublic: true)!;
            type.GetProperty("Estado")!.SetValue(response, status);
            type.GetProperty("Resultados")!.SetValue(response, Array.Empty<SelectorDestino.Sugerencia>());
            return ValueTask.FromResult((TValue)response);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
