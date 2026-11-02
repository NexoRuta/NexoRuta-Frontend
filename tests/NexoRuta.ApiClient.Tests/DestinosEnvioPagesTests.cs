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

    [Fact]
    public async Task ContratoPublicaBusquedaAutenticadaSinCambiarLaRespuestaDePropuestas()
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Contracts", "nexoruta.openapi.json")));
        var address = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ConfirmarDestinoRequest").GetProperty("properties").GetProperty("direccion");
        Assert.Contains(address.GetProperty("type").EnumerateArray(), x => x.GetString() == "string");
        var paths = document.RootElement.GetProperty("paths");
        var search = paths.GetProperty("/api/envios/{id}/destino/busqueda").GetProperty("post");
        var schema = search.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        var query = schema.GetProperty("properties").GetProperty("consulta");
        Assert.Equal(3, query.GetProperty("minLength").GetInt32());
        Assert.Equal(240, query.GetProperty("maxLength").GetInt32());
        foreach (var status in new[] { "200", "400", "401", "403", "404", "409", "429", "503" })
            Assert.True(search.GetProperty("responses").TryGetProperty(status, out _));
        var expected = paths.GetProperty("/api/envios/{id}/destino/propuestas").GetProperty("post").GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString();
        Assert.Equal(expected, search.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
    }

    [Theory]
    [InlineData(200, null)]
    [InlineData(401, "No tenés acceso")]
    [InlineData(429, "límite temporal")]
    [InlineData(503, "no está habilitada")]
    public async Task BusquedaDistingueSinResultadosDeErroresDelProveedor(int status, string? expected)
    {
        using var http = new HttpClient(new Handler(_ => Respuesta(status, status == 200
            ? "{\"envioId\":\"" + EnvioId + "\",\"direccion\":\"Registrada\",\"version\":0,\"atribucion\":\"Geoapify\",\"propuestas\":[]}"
            : "{}"))) { BaseAddress = new Uri("http://api.test/") };
        var picker = Picker(http);
        await Buscar(picker, "Montevideo");
        var message = Field<string?>(picker, "mensajeBusqueda");
        var noResults = (bool)typeof(SelectorDestino).GetProperty("sinResultados", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(picker)!;
        if (expected is null) { Assert.Null(message); Assert.True(noResults); }
        else { Assert.Contains(expected, message); Assert.False(noResults); }
        Assert.False(Field<bool>(picker, "buscando"));
    }

    [Fact]
    public async Task ConsultasCortasYDebounceNoConsumenYLaRespuestaAntiguaNoReemplazaLaNueva()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var http = new HttpClient(new AsyncHandler(async request =>
        {
            Interlocked.Increment(ref calls);
            var body = await request.Content!.ReadAsStringAsync();
            if (body.Contains("Anterior")) { started.SetResult(); return await pending.Task; }
            return Respuesta(200, Propuestas("Nueva"));
        })) { BaseAddress = new Uri("http://api.test/") };
        var picker = Picker(http);
        await Buscar(picker, "ab"); Assert.Equal(0, calls);
        var cancelledDebounce = Buscar(picker, "Nunca enviada");
        await Buscar(picker, "a"); await cancelledDebounce; Assert.Equal(0, calls);
        var old = Buscar(picker, "Anterior"); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Buscar(picker, "Nueva");
        pending.SetResult(Respuesta(200, Propuestas("Anterior"))); await old;
        Assert.Equal("Nueva", Assert.Single(Field<List<SelectorDestino.Sugerencia>>(picker, "sugerencias")).Descripcion);
        Assert.Equal(2, calls);
        await picker.DisposeAsync();
    }

    [Fact]
    public async Task ElegirResultadoSeleccionaCoordenadasYDireccionSinRepetirBusqueda()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            calls++;
            Assert.Equal($"/api/envios/{EnvioId}/destino/busqueda", request.RequestUri!.AbsolutePath);
            Assert.Equal(ApiToken, request.Headers.Authorization?.Parameter);
            Assert.Contains("consulta", request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return Respuesta(200, Propuestas("Calle sint?tica 123"));
        })) { BaseAddress = new Uri("http://api.test/") };
        var picker = Picker(http);
        (double? Latitud, double? Longitud, string? Descripcion) point = default;
        picker.PuntoCambiado = EventCallback.Factory.Create<(double?, double?, string?)>(this,
            (Action<(double?, double?, string?)>)(p => point = p));
        await Buscar(picker, "Calle");
        var suggestion = Assert.Single(Field<List<SelectorDestino.Sugerencia>>(picker, "sugerencias"));
        await (Task)typeof(SelectorDestino).GetMethod("ElegirAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(picker, [suggestion])!;
        Assert.Equal((-34.9, -56.2, "Calle sint?tica 123"), point);
        Assert.Equal(1, calls);
        Assert.Empty(Field<List<SelectorDestino.Sugerencia>>(picker, "sugerencias"));
        Assert.False((bool)typeof(SelectorDestino).GetProperty("sinResultados", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(picker)!);
    }

    private static string Propuestas(string description) => JsonSerializer.Serialize(new
    { envioId = EnvioId, direccion = "Registrada", version = 0, atribucion = "Geoapify", propuestas = new[] { new { descripcion = description, latitud = -34.9, longitud = -56.2 } } });
    private static SelectorDestino Picker(HttpClient http)
    {
        var picker = new SelectorDestino { Editable = true, EnvioId = EnvioId, ApiToken = ApiToken };
        typeof(SelectorDestino).GetProperty("Destinos", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(picker, new DestinosEnvioApiClient(http));
        return picker;
    }
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static Task Buscar(SelectorDestino picker, string text) => (Task)typeof(SelectorDestino).GetMethod("CambiarBusqueda", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(picker, [new ChangeEventArgs { Value = text }])!;
    private sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request); }

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
        await client.ConfirmarAsync(EnvioId, current.Version, -34.9, -56.2, ApiToken, direccion: "Elegida 123");

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
        Assert.Equal("Elegida 123", body.RootElement.GetProperty("direccion").GetString());
        Assert.Equal(4, body.RootElement.EnumerateObject().Count());
    }

    private static HttpResponseMessage Respuesta(int status, string body) => new((HttpStatusCode)status)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

}
