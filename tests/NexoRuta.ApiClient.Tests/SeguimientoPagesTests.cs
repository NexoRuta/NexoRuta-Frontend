using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Clients;
using NexoRuta.ApiClient.Features.Seguimiento.Clients;
using NexoRuta.Commerce.Components.Features.Envios.Pages;
using NexoRuta.Tracking.Components.Pages;

namespace NexoRuta.ApiClient.Tests;

public sealed partial class SeguimientoPagesTests
{
    private static readonly Guid Acceso = Guid.NewGuid();
    private static readonly Guid Envio = Guid.NewGuid();
    private static readonly string Token = new('b', 64);
    private const string ApiToken = "commerce-session";

    [Theory]
    [InlineData(200, null)]
    [InlineData(202, "El seguimiento se está generando. Intentá nuevamente en unos segundos")]
    [InlineData(410, "El enlace de seguimiento expiró o fue revocado.")]
    [InlineData(401, "session-expired")]
    [InlineData(403, "No pudimos abrir el seguimiento. Intentá nuevamente más tarde.")]
    [InlineData(503, "No pudimos abrir el seguimiento. Intentá nuevamente más tarde.")]
    [InlineData(0, "No pudimos abrir el seguimiento. Intentá nuevamente más tarde.")]
    public async Task Commerce_ListaYAbreSoloSeguimientoVigente(int status, string? mensaje)
    {
        var page = new MisEnvios();
        var navigation = new TestNavigation();
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal(ApiToken, request.Headers.Authorization.Parameter);
            if (request.RequestUri!.AbsolutePath == "/api/usuarios/actual/permisos") return Respuesta(200, "[]");
            if (request.RequestUri!.AbsolutePath == "/api/envios")
                return Respuesta(200, JsonSerializer.Serialize(new[] { new { id = Envio,
                    operadorNombre = "Operador de prueba", estado = "Admitido", bultos = Array.Empty<object>() } }));
            Assert.Equal($"/api/envios/{Envio}/seguimiento", request.RequestUri.AbsolutePath);
            if (status == 0) throw new HttpRequestException("PRIVATE_TECHNICAL_ERROR");
            return Respuesta(status, JsonSerializer.Serialize(new { estado = "vigente",
                url = $"http://api.test/api/public/seguimiento/{Token}", expiraEnUtc = DateTimeOffset.UtcNow.AddHours(1) }));
        })) { BaseAddress = new Uri("http://api.test/") };
        var services = Servicios(page);
        services.AddSingleton(new EnviosApiClient(http));
        services.AddSingleton(new UsuariosApiClient(http));
        services.AddSingleton<AuthenticationStateProvider>(new TestAuthentication());
        services.AddSingleton<NavigationManager>(navigation);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Tracking:BaseAddress"] = "http://tracking.test/" }).Build());
        using var provider = services.BuildServiceProvider();
        await using var renderer = new TestHtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderAsync<MisEnvios>();
            Assert.Contains("Operador de prueba", root.ToHtmlString());
            Assert.Contains("Ver seguimiento", root.ToHtmlString());
            Assert.DoesNotContain("Cancelar env", root.ToHtmlString());
            Assert.DoesNotContain(Token, root.ToHtmlString());
            Assert.DoesNotContain(ApiToken, root.ToHtmlString());
            // Invoke the same handler bound to the rendered button, without adding a UI test dependency.
            var handler = typeof(MisEnvios).GetMethod("VerSeguimientoAsync",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            await ((IHandleEvent)page).HandleEventAsync(new EventCallbackWorkItem(
                (Func<Task>)(() => (Task)handler.Invoke(page, [Envio])!)), null);
            if (mensaje == "session-expired")
            {
                Assert.Equal("/ingresar?expirada=true", navigation.Destination);
                Assert.True(navigation.ForceLoad);
            }
            else if (mensaje is null)
            {
                Assert.Equal($"http://tracking.test/seguimiento/{Token}", navigation.Destination);
                Assert.True(navigation.ForceLoad);
            }
            else
            {
                Assert.Null(navigation.Destination);
                Assert.Contains(mensaje, WebUtility.HtmlDecode(root.ToHtmlString()));
                Assert.DoesNotContain("PRIVATE_TECHNICAL_ERROR", root.ToHtmlString());
            }
        });
    }

    [Theory]
    [InlineData(200, "Estado actual:")]
    [InlineData(404, "El enlace no existe, venció o fue revocado.")]
    [InlineData(429, "Demasiadas consultas.")]
    [InlineData(500, "No pudimos consultar el estado.")]
    [InlineData(0, "No pudimos consultar el estado.")]
    public async Task Tracking_ConsultaAnonimaYNoExponeDatosPrivados(int status, string mensaje)
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal($"/api/public/seguimiento/{Token}", request.RequestUri!.AbsolutePath);
            Assert.False(request.Headers.Contains("X-NexoRuta-Acceso"));
            Assert.Null(request.Headers.Authorization);
            if (status == 0) throw new HttpRequestException("PRIVATE_TECHNICAL_ERROR");
            return Respuesta(status, """{"estado":"Admitido","direccion":"PRIVATE_ADDRESS","email":"PRIVATE_EMAIL"}""");
        })) { BaseAddress = new Uri("http://api.test/") };
        var services = Servicios(new Seguimiento());
        services.AddSingleton(new SeguimientoApiClient(http));
        using var provider = services.BuildServiceProvider();
        await using var renderer = new TestHtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderAsync<Seguimiento>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["Token"] = Token }));
            var html = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains(mensaje, html);
            if (status == 200) Assert.Contains("Admitido", html);
            Assert.DoesNotContain("PRIVATE_", html);
            Assert.DoesNotContain(Token, html);
            Assert.DoesNotContain("PRIVATE_TECHNICAL_ERROR", html);
        });
    }

    private static ServiceCollection Servicios(IComponent page)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IComponentActivator>(new PageActivator(page));
        return services;
    }

    private static HttpResponseMessage Respuesta(int status, string body) => new((HttpStatusCode)status)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class PageActivator(IComponent page) : IComponentActivator
    {
        public IComponent CreateInstance(Type type) => type == page.GetType() ? page : (IComponent)Activator.CreateInstance(type)!;
    }

    // ponytail: render the real components without a circuit; browser transport is a separate smoke check.
    private sealed class TestHtmlRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
        : StaticHtmlRenderer(services, loggerFactory)
    {
        protected override IComponent ResolveComponentForRenderMode(Type type, int? parentId,
            IComponentActivator activator, IComponentRenderMode mode) => activator.CreateInstance(type);

        public async Task<HtmlRootComponent> RenderAsync<T>(ParameterView? parameters = null) where T : IComponent
        {
            var root = BeginRenderingComponent(typeof(T), parameters ?? ParameterView.Empty);
            await root.QuiescenceTask;
            return root;
        }
    }

    private sealed class TestAuthentication : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(SesionUsuario.ClaimAccesoId, Acceso.ToString()),
                new Claim(SesionUsuario.ClaimApiToken, ApiToken)
            ], "test"))));
    }

    private sealed class TestNavigation : NavigationManager
    {
        public string? Destination { get; private set; }
        public bool ForceLoad { get; private set; }
        public TestNavigation() => Initialize("http://commerce.test/", "http://commerce.test/envios");
        protected override void NavigateToCore(string uri, bool forceLoad) { Destination = uri; ForceLoad = forceLoad; }
    }
}
