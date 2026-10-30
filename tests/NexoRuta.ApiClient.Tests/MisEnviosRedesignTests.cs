using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.ApiClient.Features.Operadores.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Clients;
using NexoRuta.Commerce.Components.Features.Envios.Pages;
using NexoRuta.Commerce.Components.Features.Envios.Shared;

namespace NexoRuta.ApiClient.Tests;

public sealed partial class SeguimientoPagesTests
{
    [Fact]
    public async Task AccionesEnvio_SeAbreConTecladoYCierraConEscape()
    {
        var menu = new AccionesEnvio();
        var services = Servicios(menu);
        using var provider = services.BuildServiceProvider();
        await using var renderer = new TestHtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderAsync<AccionesEnvio>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["EnvioId"] = Guid.NewGuid(), ["PuedeCancelar"] = true
            }));
            Assert.Contains("aria-expanded=\"false\"", root.ToHtmlString());
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var toggle = typeof(AccionesEnvio).GetMethod("AlternarMenu", flags)!;
            await ((IHandleEvent)menu).HandleEventAsync(new EventCallbackWorkItem((Action)(() => toggle.Invoke(menu, null))), null);
            Assert.Contains("Cancelar envío", WebUtility.HtmlDecode(root.ToHtmlString()));
            Assert.Contains("Ver identificador completo", WebUtility.HtmlDecode(root.ToHtmlString()));

            var keydown = typeof(AccionesEnvio).GetMethod("ManejarTecla", flags)!;
            await ((IHandleEvent)menu).HandleEventAsync(new EventCallbackWorkItem((Action)(() =>
                keydown.Invoke(menu, [new KeyboardEventArgs { Key = "Escape" }]))), null);
            Assert.Contains("aria-expanded=\"false\"", root.ToHtmlString());
            Assert.DoesNotContain("Cancelar envío", WebUtility.HtmlDecode(root.ToHtmlString()));
        });
    }

    [Fact]
    public async Task AccionesEnvio_MuestraElIdentificadorDentroDelPopoverAcotadoYEscapeLoCierra()
    {
        var envioId = Guid.Parse("a72d31c2-42fb-4ddd-a86b-ec05732a64db");
        var menu = new AccionesEnvio();
        var services = Servicios(menu);
        using var provider = services.BuildServiceProvider();
        await using var renderer = new TestHtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderAsync<AccionesEnvio>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["EnvioId"] = envioId,
                ["PuedeCancelar"] = false
            }));
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var mostrarId = typeof(AccionesEnvio).GetMethod("MostrarIdentificador", flags)!;
            await ((IHandleEvent)menu).HandleEventAsync(new EventCallbackWorkItem((Action)(() => mostrarId.Invoke(menu, null))), null);

            var expandedHtml = root.ToHtmlString();
            Assert.Contains("aria-expanded=\"true\"", expandedHtml);
            var popoverStart = expandedHtml.IndexOf("nr-shipment-more-popover", StringComparison.Ordinal);
            var identifierStart = expandedHtml.IndexOf("<code", popoverStart, StringComparison.Ordinal);
            var popoverEnd = expandedHtml.IndexOf("</div>", popoverStart, StringComparison.Ordinal);
            Assert.True(popoverStart >= 0 && identifierStart > popoverStart && popoverEnd > identifierStart,
                "The complete identifier should remain inside its bounded popover.");
            Assert.Contains(envioId.ToString(), WebUtility.HtmlDecode(expandedHtml));

            var keydown = typeof(AccionesEnvio).GetMethod("ManejarTecla", flags)!;
            await ((IHandleEvent)menu).HandleEventAsync(new EventCallbackWorkItem((Action)(() =>
                keydown.Invoke(menu, [new KeyboardEventArgs { Key = "Escape" }]))), null);

            var closedHtml = root.ToHtmlString();
            Assert.Contains("aria-expanded=\"false\"", closedHtml);
            Assert.DoesNotContain(envioId.ToString(), WebUtility.HtmlDecode(closedHtml));
        });
    }

    [Fact]
    public async Task MisEnvios_MuestraReferenciaContactoDestinoYEstadosReales()
    {
        var operadorId = Guid.NewGuid();
        var shipments = new object[]
        {
            new { id = Guid.NewGuid(), operadorId, operadorNombre = "Nexo Demo", destinatarioNombre = "Ana Pérez",
                destinatarioEmail = "ana@example.test", referenciaExterna = "PED-2026-0001", direccion = "Av. 18 de Julio 1234",
                estado = "Admitido", destinoConfirmado = false,
                bultos = new[] { new { codigo = "B-1", pesoGramos = 1250, largoCentimetros = 10, anchoCentimetros = 10, altoCentimetros = 10 } } },
            new { id = Guid.NewGuid(), operadorId, operadorNombre = "Nexo Demo", destinatarioNombre = "Bruno Silva",
                destinatarioEmail = (string?)null, referenciaExterna = (string?)null, direccion = "Calle de prueba 2",
                estado = "Cancelado", destinoConfirmado = true, bultos = Array.Empty<object>() }
        };
        var page = new MisEnvios();
        using var http = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == $"/api/operadores/{operadorId}/identidad-visual") return Respuesta(404, "{}");
            if (path == "/api/usuarios/actual/permisos") return Respuesta(200, "[\"envios.crear\"]");
            Assert.Equal("/api/envios", path);
            return Respuesta(200, JsonSerializer.Serialize(shipments));
        })) { BaseAddress = new Uri("http://api.test/") };
        var services = Servicios(page);
        services.AddSingleton(new EnviosApiClient(http));
        services.AddSingleton(new UsuariosApiClient(http));
        services.AddSingleton(new OperadoresApiClient(http));
        services.AddSingleton<AuthenticationStateProvider>(new TestAuthentication());
        services.AddSingleton<NavigationManager>(new TestNavigation());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        await using var renderer = new TestHtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderAsync<MisEnvios>();
            var html = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("PED-2026-0001", html);
            Assert.Contains("ana@example.test", html);
            Assert.Contains("Av. 18 de Julio 1234", html);
            Assert.Contains("Destino pendiente", html);
            Assert.Contains("1,25 kg", html);
            Assert.Contains("Cancelado", html);
            Assert.Contains("Mostrando 1–2 de 2 envíos", html);
            Assert.DoesNotContain("Identity server error", html);
        });
    }

    [Fact]
    public async Task MisEnvios_ErrorDeListadoMuestraUnSoloAvisoComprensibleConReintento()
    {
        var page = new MisEnvios();
        var listRequests = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/envios")
                return ++listRequests == 1 ? Respuesta(500, "permission denied") : Respuesta(200, "[]");
            if (request.RequestUri.AbsolutePath == "/api/usuarios/actual/permisos")
                return Respuesta(200, "[]");
            Assert.Fail($"Unexpected request to {request.RequestUri!.AbsolutePath}");
            return Respuesta(500, "{}");
        })) { BaseAddress = new Uri("http://api.test/") };
        var services = Servicios(page);
        services.AddSingleton(new EnviosApiClient(http));
        services.AddSingleton(new UsuariosApiClient(http));
        services.AddSingleton(new OperadoresApiClient(http));
        services.AddSingleton<AuthenticationStateProvider>(new TestAuthentication());
        services.AddSingleton<NavigationManager>(new TestNavigation());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        await using var renderer = new TestHtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderAsync<MisEnvios>();
            var html = WebUtility.HtmlDecode(root.ToHtmlString());
            const string friendlyMessage = "No pudimos cargar tus envíos. Intentá nuevamente.";
            Assert.Equal(1, html.Split(friendlyMessage, StringSplitOptions.None).Length - 1);
            Assert.DoesNotContain("class=\"nr-shipments-message\"", html);
            Assert.Contains("Reintentar", html);
            Assert.DoesNotContain("No se pudo completar la operación (HTTP 500)", html);

            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var retry = typeof(MisEnvios).GetMethod("CargarAsync", flags)!;
            await ((IHandleEvent)page).HandleEventAsync(new EventCallbackWorkItem((Func<Task>)(() =>
                (Task)retry.Invoke(page, null)!)), null);
            html = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Equal(2, listRequests);
            Assert.Contains("Todavía no tenés envíos", html);
            Assert.DoesNotContain(friendlyMessage, html);
        });
    }

    [Fact]
    public async Task MisEnvios_BusquedaPorDireccionYFiltroDeCanceladosOperanSobreElListadoCompleto()
    {
        var operadorId = Guid.NewGuid();
        var shipments = Enumerable.Range(1, 7).Select(index => new
        {
            id = Guid.NewGuid(), operadorId, operadorNombre = "Nexo Demo", destinatarioNombre = $"Persona {index}",
            destinatarioEmail = $"persona{index}@example.test", referenciaExterna = $"PED-2026-{index:0000}",
            direccion = $"Calle {index}", estado = index == 7 ? "Cancelado" : "Admitido", destinoConfirmado = true,
            bultos = Array.Empty<object>()
        }).ToArray();
        var page = new MisEnvios();
        using var http = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == $"/api/operadores/{operadorId}/identidad-visual") return Respuesta(404, "{}");
            if (path == "/api/usuarios/actual/permisos") return Respuesta(200, "[]");
            Assert.Equal("/api/envios", path);
            return Respuesta(200, JsonSerializer.Serialize(shipments));
        })) { BaseAddress = new Uri("http://api.test/") };
        var services = Servicios(page);
        services.AddSingleton(new EnviosApiClient(http));
        services.AddSingleton(new UsuariosApiClient(http));
        services.AddSingleton(new OperadoresApiClient(http));
        services.AddSingleton<AuthenticationStateProvider>(new TestAuthentication());
        services.AddSingleton<NavigationManager>(new TestNavigation());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        await using var renderer = new TestHtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderAsync<MisEnvios>();
            Assert.Contains("Mostrando 1–6 de 7 envíos", WebUtility.HtmlDecode(root.ToHtmlString()));
            Assert.DoesNotContain("PED-2026-0007", root.ToHtmlString());

            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var pageHandler = typeof(MisEnvios).GetMethod("IrAPagina", flags)!;
            await ((IHandleEvent)page).HandleEventAsync(new EventCallbackWorkItem((Func<Task>)(() =>
                (Task)pageHandler.Invoke(page, [2])!)), null);
            Assert.Contains("PED-2026-0007", root.ToHtmlString());

            var searchHandler = typeof(MisEnvios).GetMethod("CambiarBusqueda", flags)!;
            await ((IHandleEvent)page).HandleEventAsync(new EventCallbackWorkItem((Func<Task>)(() =>
                (Task)searchHandler.Invoke(page, [new ChangeEventArgs { Value = "Calle 7" }])!)), null);
            var html = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("PED-2026-0007", html);
            Assert.Contains("Mostrando 1–1 de 1 envíos", html);
            Assert.DoesNotContain("PED-2026-0006", html);

            var clearHandler = typeof(MisEnvios).GetMethod("LimpiarFiltros", flags)!;
            await ((IHandleEvent)page).HandleEventAsync(new EventCallbackWorkItem((Func<Task>)(() =>
                (Task)clearHandler.Invoke(page, null)!)), null);
            var statusHandler = typeof(MisEnvios).GetMethod("CambiarEstado", flags)!;
            await ((IHandleEvent)page).HandleEventAsync(new EventCallbackWorkItem((Func<Task>)(() =>
                (Task)statusHandler.Invoke(page, ["Cancelados"])!)), null);
            html = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("PED-2026-0007", html);
            Assert.Contains("Mostrando 1–1 de 1 envíos", html);
            Assert.DoesNotContain("PED-2026-0006", html);
        });
    }
}
