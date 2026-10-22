using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Tarifas.Clients;
using NexoRuta.ApiClient.Features.Tarifas.Contracts;
using NexoRuta.ApiClient.Features.ZonasCobertura.Clients;
using NexoRuta.Backoffice.Features.Tarifas;

namespace NexoRuta.Backoffice.Features.Tarifas.Pages;

public sealed class PublicarModel(TarifasApiClient tarifasApi, ZonasCoberturaApiClient zonasApi) : TarifasPageModel
{
    [BindProperty] public string VersionConcurrencia { get; set; } = "";
    [BindProperty] public bool ConfirmarPublicacion { get; set; }
    public VersionTarifarioResponse? Borrador { get; private set; }
    public IReadOnlyList<ZonaTarifarioOption> Zonas { get; private set; } = [];
    [TempData] public string? Notice { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await LoadCurrentDraftAsync(setExpectedVersion: true);
        return result ?? Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ConfirmarPublicacion)
            ModelState.AddModelError(nameof(ConfirmarPublicacion), "Confirmá que querés publicar esta configuración inmutable.");
        if (!long.TryParse(VersionConcurrencia, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion))
            ModelState.AddModelError(nameof(VersionConcurrencia), "Recargá el borrador antes de publicar.");
        if (!ModelState.IsValid)
        {
            var loadResult = await LoadCurrentDraftAsync(setExpectedVersion: false);
            return loadResult ?? Page();
        }

        try
        {
            var published = await tarifasApi.PublicarBorradorAsync(
                new PublicarBorradorTarifarioRequest(expectedVersion),
                SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            Notice = $"Se publicó la versión {published.NumeroVersionPublicada}. La publicación es de solo lectura.";
            return RedirectToPage("/Tarifas/Publicaciones/Detalle", new { versionId = published.VersionTarifarioId });
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await HandleApiErrorAsync(exception);
            if (redirect is not null) return redirect;
        }
        catch (HttpRequestException) { HandleTransportError(); }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { HandleTransportError(); }

        var currentDraft = await LoadCurrentDraftAsync(setExpectedVersion: false);
        return currentDraft ?? Page();
    }

    public string ZonaNombre(Guid id)
        => Zonas.FirstOrDefault(zone => zone.Id == id)?.Nombre ?? "Zona sin nombre disponible";

    public bool ZonaInactiva(Guid id)
        => Zonas.FirstOrDefault(zone => zone.Id == id)?.Estado == TarifasUi.InactiveZone;

    private async Task<IActionResult?> LoadCurrentDraftAsync(bool setExpectedVersion)
    {
        var token = SesionUsuario.ObtenerToken(User);
        try
        {
            Borrador = await tarifasApi.ObtenerBorradorAsync(token, HttpContext.RequestAborted);
            if (setExpectedVersion)
                VersionConcurrencia = Borrador.VersionConcurrencia.ToString(CultureInfo.InvariantCulture);
        }
        catch (NexoRutaApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Notice = "No hay un borrador para publicar.";
            return RedirectToPage("/Tarifas/Index");
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
        return null;
    }
}
