using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.ApiClient.Features.Envios.Contracts.Responses;
using NexoRuta.Commerce.Components.Features.Envios.Pages;

namespace NexoRuta.ApiClient.Tests;

public sealed partial class SeguimientoPagesTests
{
    [Fact]
    public async Task DestinoExigeSeleccionYConfirmacionExpresaConVersionDeDireccion()
    {
        var page = new DestinoEnvio(); var confirmaciones = 0;
        using var http = new HttpClient(new Handler(request => {
            Assert.Equal(ApiToken, request.Headers.Authorization!.Parameter);
            if (request.Method == HttpMethod.Get)
                return Respuesta(200, JsonSerializer.Serialize(new { EnvioId = Envio, Direccion = "Calle de prueba 123", Version = confirmaciones == 0 ? 4 : 5,
                    Estado = "Admitido", Latitud = confirmaciones == 0 ? (double?)null : -34.9, Longitud = confirmaciones == 0 ? (double?)null : -56.2,
                    ConfirmadaEnUtc = confirmaciones == 0 ? (DateTimeOffset?)null : DateTimeOffset.UtcNow }));
            if (request.Method == HttpMethod.Post)
                return Respuesta(200, JsonSerializer.Serialize(new { EnvioId = Envio, Direccion = "Calle de prueba 123", Version = 4, Atribucion = "Geoapify · OSM",
                    Propuestas = new[] { new { Descripcion = "Primera coincidencia", Latitud = -34.9, Longitud = -56.2 }, new { Descripcion = "Segunda coincidencia", Latitud = -34.8, Longitud = -56.1 } } }));
            Assert.Equal(HttpMethod.Put, request.Method);
            var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement;
            Assert.Equal(4, body.GetProperty("version").GetInt32()); Assert.Equal(-34.9, body.GetProperty("latitud").GetDouble());
            Assert.Equal(-56.2, body.GetProperty("longitud").GetDouble()); Assert.Equal(3, body.EnumerateObject().Count());
            confirmaciones++; return Respuesta(204, "");
        })) { BaseAddress = new Uri("http://api.test/") };
        var services = Servicios(page); services.AddSingleton(new DestinosEnvioApiClient(http));
        services.AddSingleton<AuthenticationStateProvider>(new TestAuthentication()); services.AddSingleton<NavigationManager>(new TestNavigation());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); services.AddSingleton<IJSRuntime>(new JsSinMapa());
        using var provider = services.BuildServiceProvider();
        await using var renderer = new TestHtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () => {
            var root = await renderer.RenderAsync<DestinoEnvio>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["EnvioId"] = Envio }));
            var html = WebUtility.HtmlDecode(root.ToHtmlString()); Assert.Contains("Primera coincidencia", html); Assert.Contains("Segunda coincidencia", html);
            Assert.Contains("Geoapify", html); Assert.DoesNotContain(ApiToken, html); Assert.Equal(0, confirmaciones);
            await Accionar("ConfirmarAsync"); Assert.Equal(0, confirmaciones);
            await ((IHandleEvent)page).HandleEventAsync(new EventCallbackWorkItem((Action)(() => typeof(DestinoEnvio).GetMethod("Elegir", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(page, [new PropuestaDestinoResponse("Primera coincidencia", -34.9, -56.2)]))), null);
            await Accionar("ConfirmarAsync"); Assert.Equal(0, confirmaciones);
            typeof(DestinoEnvio).GetField("revisado", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, true);
            await Accionar("ConfirmarAsync"); Assert.Equal(1, confirmaciones);
            Assert.Contains("Ubicación confirmada", WebUtility.HtmlDecode(root.ToHtmlString()));
        });
        Task Accionar(string nombre) => ((IHandleEvent)page).HandleEventAsync(new EventCallbackWorkItem((Func<Task>)(() =>
            (Task)typeof(DestinoEnvio).GetMethod(nombre, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, null)!)), null);
    }
    [Theory]
    [InlineData(200, "No encontramos coincidencias")]
    [InlineData(503, "No se pudo consultar la dirección")]
    [InlineData(429, "Esperá un minuto")]
    public async Task DestinoNoInventaPuntoCuandoProveedorNoPuedeResolver(int status, string mensaje)
    {
        using var http = new HttpClient(new Handler(request => request.Method == HttpMethod.Get
            ? Respuesta(200, JsonSerializer.Serialize(new { EnvioId = Envio, Direccion = "Calle de prueba", Version = 0, Estado = "Admitido" }))
            : Respuesta(status, JsonSerializer.Serialize(new { EnvioId = Envio, Direccion = "Calle de prueba", Version = 0, Atribucion = "Geoapify", Propuestas = Array.Empty<object>() }))))
            { BaseAddress = new Uri("http://api.test/") };
        var services = Servicios(new DestinoEnvio()); services.AddSingleton(new DestinosEnvioApiClient(http));
        services.AddSingleton<AuthenticationStateProvider>(new TestAuthentication()); services.AddSingleton<NavigationManager>(new TestNavigation());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); services.AddSingleton<IJSRuntime>(new JsSinMapa());
        using var provider = services.BuildServiceProvider();
        await using var renderer = new TestHtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () => {
            var root = await renderer.RenderAsync<DestinoEnvio>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["EnvioId"] = Envio }));
            var html = WebUtility.HtmlDecode(root.ToHtmlString()); Assert.Contains(mensaje, html);
            Assert.DoesNotContain("Ubicación confirmada", html); Assert.Contains("disabled", html);
        });
    }
    private sealed class JsSinMapa : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}
