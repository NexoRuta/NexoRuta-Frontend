using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.ApiClient.Features.Envios.Contracts.Responses;
using NexoRuta.ApiClient.Features.Usuarios.Clients;
using NexoRuta.Commerce.Components.Features.Envios.Pages;

namespace NexoRuta.ApiClient.Tests;

public sealed partial class SeguimientoPagesTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(409)]
    [InlineData(401)]
    public async Task ComercioConfirmaCancelacionConMotivoYVersionSinExponerCredenciales(int estadoRespuesta)
    {
        var page = new MisEnvios(); var navigation = new TestNavigation();
        var estado = "EnTransito"; var llamadas = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal(ApiToken, request.Headers.Authorization!.Parameter);
            if (request.RequestUri!.AbsolutePath == "/api/usuarios/actual/permisos") return Respuesta(200, "[\"envios.crear\"]");
            if (request.RequestUri.AbsolutePath == "/api/envios") return Respuesta(200, JsonSerializer.Serialize(new[] {
                new { id = Envio, version = 8, operadorNombre = "Operador de prueba", estado, bultos = Array.Empty<object>() } }));
            Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal($"/api/envios/{Envio}/cancelacion", request.RequestUri.AbsolutePath);
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal(8, body.RootElement.GetProperty("version").GetInt64());
            Assert.Equal("Cambio solicitado por el comercio.", body.RootElement.GetProperty("motivo").GetString()); llamadas++;
            if (estadoRespuesta == 200) estado = "Cancelado";
            return Respuesta(estadoRespuesta, estadoRespuesta == 200
                ? JsonSerializer.Serialize(new { cancelacionId = Guid.NewGuid(), envioId = Envio, version = 9, estado })
                : "{\"detail\":\"El envío cambió. Actualizá su estado antes de cancelar.\"}");
        })) { BaseAddress = new Uri("http://api.test/") };
        var services = Servicios(page); services.AddSingleton(new EnviosApiClient(http)); services.AddSingleton(new UsuariosApiClient(http));
        services.AddSingleton<AuthenticationStateProvider>(new TestAuthentication()); services.AddSingleton<NavigationManager>(navigation);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        await using var renderer = new TestHtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderAsync<MisEnvios>();
            Assert.Contains("Cancelar envío", WebUtility.HtmlDecode(root.ToHtmlString()));
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var envio = Assert.Single((IReadOnlyList<EnvioResponse>)typeof(MisEnvios).GetField("envios", flags)!.GetValue(page)!);
            var preparar = typeof(MisEnvios).GetMethod("PrepararCancelacion", flags)!;
            await ((IHandleEvent)page).HandleEventAsync(new EventCallbackWorkItem((Action)(() => preparar.Invoke(page, [envio]))), null);
            Assert.Contains("Confirmar cancelación", WebUtility.HtmlDecode(root.ToHtmlString()));
            Assert.Equal(0, llamadas);
            typeof(MisEnvios).GetField("motivoCancelacion", flags)!.SetValue(page, "Cambio solicitado por el comercio.");
            var confirmar = typeof(MisEnvios).GetMethod("ConfirmarCancelacionAsync", flags)!;
            await ((IHandleEvent)page).HandleEventAsync(new EventCallbackWorkItem((Func<Task>)(() => (Task)confirmar.Invoke(page, null)!)), null);
            Assert.Equal(1, llamadas);
            if (estadoRespuesta == 200)
            {
                Assert.Contains("Cancelado", root.ToHtmlString()); Assert.DoesNotContain("Confirmar cancelaci", root.ToHtmlString());
            }
            else if (estadoRespuesta == 401) Assert.Equal("/ingresar?expirada=true", navigation.Destination);
            else Assert.Contains("El envío cambió", WebUtility.HtmlDecode(root.ToHtmlString()));
            Assert.DoesNotContain(ApiToken, root.ToHtmlString());
        });
    }
}
