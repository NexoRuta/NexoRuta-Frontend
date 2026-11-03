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
    public async Task RenderWhileMapCreationIsPendingDoesNotCreateAnotherMapOrKeyboardBinding()
    {
        var js = new PausedMapCreation();
        var picker = new SelectorDestino { Editable = true };
        var type = typeof(SelectorDestino);
        type.GetProperty("Js", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(picker, js);
        type.GetField("token", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(picker, "pk.synthetic");
        Task Render() => (Task)type.GetMethod("OnAfterRenderAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(picker, [false])!;
        var first = Render();
        await js.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = Render();
        try
        {
            Assert.Equal(1, js.Creations);
            Assert.Equal(1, js.Keyboards);
        }
        finally
        {
            js.Continue.TrySetResult();
            await Task.WhenAll(first, second);
        }
        await Render();
        Assert.Equal(1, js.Creations);
        Assert.Equal(1, js.Keyboards);
        await picker.DisposeAsync();
    }

    private sealed class PausedMapCreation : IJSRuntime, IJSObjectReference
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Creations, Keyboards;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "crear")
            {
                Creations++;
                Started.TrySetResult();
                await Continue.Task.WaitAsync(cancellationToken);
            }
            if (identifier == "conectarTeclado") Keyboards++;
            return typeof(TValue) == typeof(IJSObjectReference) ? (TValue)(object)this : default!;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task CoberturaDistingueCargaSinZonasYErrorDeConsulta()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new AsyncHandler(_ => pending.Task)) { BaseAddress = new Uri("http://api.test/") };
        var picker = new SelectorDestino { Editable = true, ApiToken = ApiToken, OperadorId = Guid.NewGuid() };
        typeof(SelectorDestino).GetProperty("Envios", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(picker, new EnviosApiClient(http));
        Task Parameters() => (Task)typeof(SelectorDestino).GetMethod("OnParametersSetAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(picker, null)!;
        var loading = Parameters();
        Assert.True(Field<bool>(picker, "cargandoCobertura"));
        pending.SetResult(Respuesta(200, "[]")); await loading;
        Assert.False(Field<bool>(picker, "cargandoCobertura")); Assert.False(Field<bool>(picker, "errorCobertura"));
        Assert.Empty(Field<IReadOnlyList<CoberturaEnvioResponse>>(picker, "cobertura"));
        using var failing = new HttpClient(new AsyncHandler(_ => Task.FromResult(Respuesta(503, "{}")))) { BaseAddress = new Uri("http://api.test/") };
        typeof(SelectorDestino).GetProperty("Envios", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(picker, new EnviosApiClient(failing));
        picker.OperadorId = Guid.NewGuid(); await Parameters();
        Assert.False(Field<bool>(picker, "cargandoCobertura")); Assert.True(Field<bool>(picker, "errorCobertura"));
        Assert.NotEmpty(Field<string>(picker, "mensajeCobertura"));
        await picker.DisposeAsync();
    }

    [Fact]
    public async Task CambioDeOperadorMientrasSeLimpiaMapaNoReutilizaTokenDescartado()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var calls = new List<string>();
        using var http = new HttpClient(new Handler(request => {
            calls.Add(request.RequestUri!.AbsolutePath);
            return Respuesta(200, "[{\"zonaId\":\"" + second + "\",\"nombre\":\"Current\",\"geometria\":{\"type\":\"MultiPolygon\",\"coordinates\":[]}}]");
        })) { BaseAddress = new Uri("http://api.test/") };
        var picker = new SelectorDestino { Editable = true, ApiToken = ApiToken, OperadorId = first };
        var type = typeof(SelectorDestino);
        type.GetProperty("Envios", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(picker, new EnviosApiClient(http));
        var map = new PausedCoverageMap();
        type.GetField("mapa", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(picker, map);
        Task Parameters() => (Task)type.GetMethod("OnParametersSetAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(picker, null)!;
        var old = Parameters(); await map.Started.Task;
        picker.OperadorId = second; await Parameters();
        map.Release.SetResult(); await old;
        Assert.Equal($"/api/operadores/{second}/cobertura-envios", Assert.Single(calls));
        Assert.Equal(second, Assert.Single(Field<IReadOnlyList<CoberturaEnvioResponse>>(picker, "cobertura")).ZonaId);
        await picker.DisposeAsync();
    }

    private sealed class PausedCoverageMap : IJSObjectReference
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int clears;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "cobertura" && Interlocked.Increment(ref clears) == 1) { Started.SetResult(); await Release.Task; }
            return default!;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task CoberturaCambiaConOperadorYDescartaRespuestaAnterior()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new AsyncHandler(request => request.RequestUri!.AbsolutePath.Contains(first.ToString())
            ? pending.Task : Task.FromResult(Respuesta(200, "[{\"zonaId\":\"" + second + "\",\"nombre\":\"Current\",\"geometria\":{\"type\":\"MultiPolygon\",\"coordinates\":[]}}]")))) { BaseAddress = new Uri("http://api.test/") };
        var picker = new SelectorDestino { Editable = true, ApiToken = ApiToken, OperadorId = first };
        typeof(SelectorDestino).GetProperty("Envios", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(picker, new EnviosApiClient(http));
        Task Parameters() => (Task)typeof(SelectorDestino).GetMethod("OnParametersSetAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(picker, null)!;
        var old = Parameters();
        picker.OperadorId = second; await Parameters();
        pending.SetResult(Respuesta(200, "[{\"zonaId\":\"" + first + "\",\"nombre\":\"Old\",\"geometria\":{\"type\":\"MultiPolygon\",\"coordinates\":[]}}]"));
        await old;
        Assert.Equal(second, Assert.Single(Field<IReadOnlyList<CoberturaEnvioResponse>>(picker, "cobertura")).ZonaId);
        picker.OperadorId = null; await Parameters();
        Assert.Empty(Field<IReadOnlyList<CoberturaEnvioResponse>>(picker, "cobertura"));
        await picker.DisposeAsync();
    }

    [Fact]
    public void DestinoConfirmadoNoRequiereOtraConfirmacionHastaCambiarElPunto()
    {
        var page = new NexoRuta.Commerce.Components.Features.Envios.Pages.DestinoEnvio();
        var type = page.GetType();
        void Set(string name, object value) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
        T Get<T>(string name) => (T)type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        Set("destino", new DestinoEnvioResponse(EnvioId, "Recipient", "Saved address", 1, "Admitido", -34.9, -56.2, DateTimeOffset.UtcNow));
        var point = type.GetField("punto", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        point.GetType().GetProperty("Latitud")!.SetValue(point, -34.9);
        point.GetType().GetProperty("Longitud")!.SetValue(point, -56.2);
        Set("puntoPersistido", true); Set("revisado", true);
        Assert.Equal("Ubicación confirmada y guardada.", Get<string>("EstadoPunto"));
        Assert.False(Get<bool>("PuedeConfirmar"));
        type.GetMethod("ActualizarPunto", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [(-34.91 as double?, -56.21 as double?, "Adjusted address")]);
        Set("revisado", true);
        Assert.True(Get<bool>("PuedeConfirmar"));
        Assert.Contains("pendiente de confirmar", Get<string>("EstadoPunto"));
    }

    [Fact]
    public async Task ContratoPublicaBusquedaAutenticadaSinCambiarLaRespuestaDePropuestas()
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Contracts", "nexoruta.openapi.json")));
        var address = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ConfirmarDestinoRequest").GetProperty("properties").GetProperty("direccion");
        Assert.Contains(address.GetProperty("type").EnumerateArray(), x => x.GetString() == "string");
        var paths = document.RootElement.GetProperty("paths");
        var search = paths.GetProperty("/api/envios/{id}/destino/busqueda").GetProperty("post");
        var schema = search.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        if (schema.TryGetProperty("$ref", out var reference))
            schema = document.RootElement.GetProperty("components").GetProperty("schemas")
                .GetProperty(reference.GetString()!.Split('/')[^1]);
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
