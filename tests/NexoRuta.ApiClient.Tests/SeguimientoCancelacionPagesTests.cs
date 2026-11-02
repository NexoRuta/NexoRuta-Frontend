using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexoRuta.ApiClient.Features.Seguimiento.Clients;
using NexoRuta.Tracking.Components.Pages;

namespace NexoRuta.ApiClient.Tests;

public sealed partial class SeguimientoPagesTests
{
    [Fact]
    public async Task TrackingSolicitaCorreoYConfirmaCodigoSinDatosPrivados()
    {
        var page = new Seguimiento(); var pedidos = new List<string>();
        using var http = new HttpClient(new Handler(request => {
            Assert.Null(request.Headers.Authorization);
            var path = request.RequestUri!.AbsolutePath; pedidos.Add(path);
            if (request.Method == HttpMethod.Get) return Respuesta(200, """{"estado":"EnTransito"}""");
            Assert.Equal(HttpMethod.Post, request.Method);
            if (path.EndsWith("/codigo")) { Assert.Null(request.Content); return Respuesta(202, ""); }
            Assert.Equal($"/api/public/seguimiento/{Token}/cancelacion", path);
            var content = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Equal("ABCD-EFGH-JKMN", JsonDocument.Parse(content).RootElement.GetProperty("codigo").GetString());
            return Respuesta(200, """{"estado":"Cancelado"}""");
        })) { BaseAddress = new Uri("http://api.test/") };
        var services = Servicios(page); services.AddSingleton(new SeguimientoApiClient(http));
        using var provider = services.BuildServiceProvider();
        await using var renderer = new TestHtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () => {
            var root = await renderer.RenderAsync<Seguimiento>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Token"] = Token }));
            Assert.Contains("Enviar c", root.ToHtmlString());
            await Accionar("SolicitarCodigo");
            var html = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("Código recibido por correo", html); Assert.Contains("cinco intentos", html);
            typeof(Seguimiento).GetField("codigo", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, "ABCD-EFGH-JKMN");
            await Accionar("Cancelar");
            html = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("El envío fue cancelado", html); Assert.DoesNotContain("Confirmar cancelación", html);
            Assert.DoesNotContain("ABCD", html); Assert.DoesNotContain(Token, html);
            Assert.Equal(3, pedidos.Count);
        });
        Task Accionar(string metodo) => ((IHandleEvent)page).HandleEventAsync(new EventCallbackWorkItem((Func<Task>)(() =>
            (Task)typeof(Seguimiento).GetMethod(metodo, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, null)!)), null);
    }

    [Theory]
    [InlineData("Entregado")] [InlineData("Devuelto")] [InlineData("Extraviado")]
    [InlineData("EntregaPendienteValidacion")] [InlineData("EntregaSinValidacion")] [InlineData("Cancelado")]
    public async Task TrackingOcultaCancelacionDespuesDeEntregaOCancelacion(string estado)
    {
        using var http = new HttpClient(new Handler(_ => Respuesta(200, JsonSerializer.Serialize(new { estado })))) { BaseAddress = new Uri("http://api.test/") };
        var services = Servicios(new Seguimiento()); services.AddSingleton(new SeguimientoApiClient(http));
        using var provider = services.BuildServiceProvider();
        await using var renderer = new TestHtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () => {
            var root = await renderer.RenderAsync<Seguimiento>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Token"] = Token }));
            Assert.DoesNotContain("cancelacion-titulo", root.ToHtmlString());
        });
    }

    [Fact]
    public async Task TrackingPermiteSolicitarCancelacionEnDevolucion()
    {
        using var http = new HttpClient(new Handler(_ => Respuesta(200, "{\"estado\":\"EnDevolucion\"}"))) { BaseAddress = new Uri("http://api.test/") };
        var services = Servicios(new Seguimiento()); services.AddSingleton(new SeguimientoApiClient(http));
        using var provider = services.BuildServiceProvider();
        await using var renderer = new TestHtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () => {
            var root = await renderer.RenderAsync<Seguimiento>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Token"] = Token }));
            Assert.Contains("cancelacion-titulo", root.ToHtmlString());
        });
    }
}
