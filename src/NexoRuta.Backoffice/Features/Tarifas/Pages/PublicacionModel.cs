using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Tarifas.Clients;
using NexoRuta.ApiClient.Features.Tarifas.Contracts;
using NexoRuta.ApiClient.Features.ZonasCobertura.Clients;
using NexoRuta.Backoffice.Features.Tarifas;
using Microsoft.AspNetCore.Mvc;

namespace NexoRuta.Backoffice.Features.Tarifas.Pages;

public sealed class PublicacionModel(TarifasApiClient tarifasApi, ZonasCoberturaApiClient zonasApi) : TarifasPageModel
{
    public VersionTarifarioResponse? Publicacion { get; private set; }
    public Guid? VersionVigenteId { get; private set; }
    public IReadOnlyList<ZonaTarifarioOption> Zonas { get; private set; } = [];
    [TempData] public string? Notice { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid versionId)
    {
        var token = SesionUsuario.ObtenerToken(User);
        try
        {
            Publicacion = await tarifasApi.ObtenerPublicacionAsync(versionId, token, HttpContext.RequestAborted);
        }
        catch (NexoRutaApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound();
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await HandleApiErrorAsync(exception);
            if (redirect is not null) return redirect;
        }
        catch (HttpRequestException) { HandleTransportError(); }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { HandleTransportError(); }

        if (Publicacion is null) return Page();
        try
        {
            var state = await tarifasApi.ObtenerEstadoAsync(token, HttpContext.RequestAborted);
            VersionVigenteId = state.PublicacionVigente?.VersionTarifarioId;
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await HandleApiErrorAsync(exception);
            if (redirect is not null) return redirect;
        }
        catch (HttpRequestException) { HandleTransportError(); }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { HandleTransportError(); }

        try { Zonas = await TarifasPageData.CargarTodasLasZonasAsync(zonasApi, token, HttpContext.RequestAborted); }
        catch (NexoRutaApiException exception)
        {
            var redirect = await HandleApiErrorAsync(exception);
            if (redirect is not null) return redirect;
        }
        catch (HttpRequestException) { HandleTransportError(); }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { HandleTransportError(); }
        return Page();
    }

    public string ZonaNombre(Guid id)
        => Zonas.FirstOrDefault(zone => zone.Id == id)?.Nombre ?? "Zona sin nombre disponible";

    public bool ZonaInactiva(Guid id)
        => Zonas.FirstOrDefault(zone => zone.Id == id)?.Estado == TarifasUi.InactiveZone;
}
