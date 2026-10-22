using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NexoRuta.ApiClient.Features.Envios.Contracts.Requests;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.ApiClient.Features.Operadores.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Clients;
using NexoRuta.Commerce.Components.Features.Envios.Pages;

namespace NexoRuta.ApiClient.Tests;

public sealed partial class SeguimientoPagesTests
{
    [Fact]
    public async Task CrearEnvioIgnoresConcurrentSubmitAndRetriesWithTheSameEnteredValues()
    {
        var page = new CrearEnvio();
        var handler = new CreateShipmentHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };
        var services = Servicios(page);
        services.AddSingleton(new EnviosApiClient(http));
        services.AddSingleton(new UsuariosApiClient(http));
        services.AddSingleton(new OperadoresApiClient(http));
        services.AddSingleton<AuthenticationStateProvider>(new TestAuthentication());
        services.AddSingleton<NavigationManager>(new TestNavigation());
        using var provider = services.BuildServiceProvider();
        await using var renderer = new TestHtmlRenderer(provider, provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(() => renderer.RenderAsync<CrearEnvio>());

        var request = (CrearEnvioRequest)typeof(CrearEnvio).GetField("formulario", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        request.OperadorId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        request.DestinatarioNombre = "Destinataria sintética";
        request.DestinatarioEmail = "destinatario@example.test";
        request.Direccion = "Calle Sintética 123";
        request.CodigoBulto = "SYN-001";
        request.PesoGramos = 1000;
        request.LargoCentimetros = 20;
        request.AnchoCentimetros = 15;
        request.AltoCentimetros = 10;

        var create = typeof(CrearEnvio).GetMethod("CrearAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var firstAttempt = renderer.Dispatcher.InvokeAsync(() => (Task)create.Invoke(page, null)!);
        await handler.FirstPostStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await renderer.Dispatcher.InvokeAsync(() => (Task)create.Invoke(page, null)!);
        Assert.Equal(1, handler.PostCount);

        handler.ReleaseFirstPost.TrySetResult();
        await firstAttempt;
        Assert.Equal("Destinataria sintética", request.DestinatarioNombre);
        Assert.Equal("destinatario@example.test", request.DestinatarioEmail);
        Assert.Equal("No se pudo guardar el envío porque el servicio no está disponible.",
            typeof(CrearEnvio).GetField("error", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));

        await renderer.Dispatcher.InvokeAsync(() => (Task)create.Invoke(page, null)!);

        Assert.Equal(2, handler.PostCount);
        Assert.Equal("Destinataria sintética", handler.RetriedName);
        Assert.Equal("destinatario@example.test", handler.RetriedEmail);
        Assert.Null(typeof(CrearEnvio).GetField("error", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
        Assert.NotNull(typeof(CrearEnvio).GetField("creado", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
    }

    private sealed class CreateShipmentHandler : HttpMessageHandler
    {
        public TaskCompletionSource FirstPostStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstPost { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int PostCount => Volatile.Read(ref _postCount);
        public string? RetriedName { get; private set; }
        public string? RetriedEmail { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/usuarios/actual")
                return Respuesta(200, """{"accesoId":"00000000-0000-0000-0000-000000000005","tipo":"Comercio","usuarioId":"00000000-0000-0000-0000-000000000001","operadorId":null,"rolId":"00000000-0000-0000-0000-000000000007","rolCodigo":"administrador_operador","comercioId":"00000000-0000-0000-0000-000000000003","usuarioEmail":"comercio@example.test","operadorNombre":null,"comercioNombre":"Comercio sintético"}""");
            if (request.RequestUri.AbsolutePath == "/api/operadores")
                return Respuesta(200, """[{"operadorId":"00000000-0000-0000-0000-000000000002","nombre":"Operador sintético"}]""");
            if (request.RequestUri.AbsolutePath != "/api/envios" || request.Method != HttpMethod.Post)
                throw new InvalidOperationException($"Unexpected API request: {request.Method} {request.RequestUri.AbsolutePath}");

            var attempt = Interlocked.Increment(ref _postCount);
            if (attempt == 1)
            {
                FirstPostStarted.TrySetResult();
                await ReleaseFirstPost.Task.WaitAsync(cancellationToken);
                throw new HttpRequestException("API unavailable");
            }

            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            RetriedName = body.RootElement.GetProperty("destinatarioNombre").GetString();
            RetriedEmail = body.RootElement.GetProperty("destinatarioEmail").GetString();
            return Respuesta(201, """{"id":"00000000-0000-0000-0000-000000000501","operadorId":"00000000-0000-0000-0000-000000000002","comercioId":"00000000-0000-0000-0000-000000000003","creadoPorUsuarioId":"00000000-0000-0000-0000-000000000004","usuarioEmail":"comercio@example.test","operadorNombre":"Operador sintético","comercioNombre":"Comercio sintético","codigoBulto":"SYN-001","pesoGramos":1000,"largoCentimetros":20,"anchoCentimetros":15,"altoCentimetros":10}""");
        }

        private int _postCount;
    }
}
