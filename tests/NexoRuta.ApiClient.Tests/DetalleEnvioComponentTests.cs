using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.ApiClient.Features.Envios.Contracts.Responses;
using NexoRuta.Commerce.Components.Features.Envios.Pages;
using NexoRuta.Commerce.Components.Features.Envios.Shared;

namespace NexoRuta.ApiClient.Tests;

public sealed partial class SeguimientoPagesTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShipmentDetailShowsOnlyTheRequestedVisibleShipment(bool visible)
    {
        var id = Guid.NewGuid();
        var page = new DetalleEnvio { Creado = true };
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("/api/envios", request.RequestUri!.AbsolutePath);
            Assert.Equal(ApiToken, request.Headers.Authorization!.Parameter);
            return Respuesta(200, System.Text.Json.JsonSerializer.Serialize(new[] {
                new EnvioResponse(visible ? id : Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                    "commerce@example.test", "Operador de prueba", "Comercio", "Destinataria de prueba",
                    "Dirección guardada 123", "Admitido", 1,
                    [new BultoResponse("BULTO-A",1500,30,20,10),new BultoResponse("BULTO-B",1000,20,20,10)], DateTimeOffset.UtcNow)
                { DestinoConfirmado = true, Tarifa = new(Guid.NewGuid(),Guid.NewGuid(),"Urgente","UYU",1751,1500,250,1,0,0) }
            }));
        })) { BaseAddress = new Uri("http://api.test/") };
        var services = Servicios(page);
        services.AddSingleton(new EnviosApiClient(http));
        services.AddSingleton<AuthenticationStateProvider>(new TestAuthentication());
        services.AddSingleton<NavigationManager>(new TestNavigation());
        using var provider = services.BuildServiceProvider();
        await using var renderer = new TestHtmlRenderer(provider, provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderAsync<DetalleEnvio>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(DetalleEnvio.EnvioId)] = id }));
            var html = WebUtility.HtmlDecode(root.ToHtmlString());
            if (visible)
            {
                Assert.Contains("Envío creado correctamente", html);
                Assert.Contains("Destinataria de prueba", html);
                Assert.Contains("Dirección guardada 123", html);
                Assert.Contains("Operador de prueba", html);
                Assert.Contains("BULTO-A", html); Assert.Contains("BULTO-B", html);
                Assert.Contains("Ubicación confirmada", html);
                Assert.Contains("Tarifa aplicada", html);
                Assert.Contains("Descargar etiquetas", html);
                Assert.Contains($"/envios/{id}/etiquetas", html);
                Assert.DoesNotContain("Confirmar ubicación", html);
                Assert.DoesNotContain("crear-envio", html);
            }
            else
            {
                Assert.Contains("No encontramos un envío", html);
                Assert.DoesNotContain("Destinataria de prueba", html);
                Assert.DoesNotContain("Envío creado correctamente", html);
            }
        });
    }

    [Fact]
    public async Task QuoteBreakdownDisplaysServerAmountsWithoutRecalculating()
    {
        var services = Servicios(new DesgloseTarifa());
        using var provider = services.BuildServiceProvider();
        await using var renderer = new TestHtmlRenderer(provider, provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var tariff = new TarifaEnvioResponse(Guid.NewGuid(),Guid.NewGuid(),"Urgente","UYU",1751,1500,250,1,0,0);
            var root = await renderer.RenderAsync<DesgloseTarifa>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(DesgloseTarifa.Tarifa)] = tariff }));
            var html = WebUtility.HtmlDecode(root.ToHtmlString());
            foreach (var label in new[] { "Base", "Peso", "Volumen", "Recargos", "Bonificaciones", "Total" }) Assert.Contains(label, html);
            foreach (var amount in new[] { tariff.ImporteBaseUyu, tariff.ComponentePesoUyu, tariff.ComponenteVolumenUyu, tariff.TotalFinalUyu })
                Assert.Contains(amount.ToString("N2") + " UYU", html);
        });
    }
}
