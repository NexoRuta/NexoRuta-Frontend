using Microsoft.AspNetCore.Mvc;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Tarifas.Clients;
using NexoRuta.Backoffice.Core.Security;
using NexoRuta.Backoffice.Features.Tarifas;

namespace NexoRuta.Backoffice.Features.Tarifas.Pages;

public sealed class IndexModel(TarifasApiClient api) : TarifasPageModel
{
    public NexoRuta.ApiClient.Features.Tarifas.Contracts.TarifarioEstadoResponse? Estado { get; private set; }
    [TempData] public string? Notice { get; set; }

    public async Task<IActionResult> OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostCrearBorradorAsync()
    {
        try
        {
            await api.CrearBorradorAsync(SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            Notice = "El borrador está listo. Revisá su contenido y guardalo antes de publicarlo.";
            return RedirectToPage("/Tarifas/Borrador");
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await HandleApiErrorAsync(exception);
            if (redirect is not null) return redirect;
        }
        catch (HttpRequestException) { HandleTransportError(); }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { HandleTransportError(); }

        var stateRedirect = await LoadStateAfterErrorAsync();
        if (stateRedirect is not null) return stateRedirect;
        return Page();
    }

    private async Task<IActionResult> LoadAsync()
    {
        try
        {
            Estado = await api.ObtenerEstadoAsync(SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            return Page();
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await HandleApiErrorAsync(exception);
            if (redirect is not null) return redirect;
        }
        catch (HttpRequestException) { HandleTransportError(); }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { HandleTransportError(); }
        return Page();
    }

    private async Task<IActionResult?> LoadStateAfterErrorAsync()
    {
        try
        {
            Estado = await api.ObtenerEstadoAsync(SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            return null;
        }
        catch (NexoRutaApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return await ApiSession.RedirectIfExpiredAsync(this, exception);
        }
        catch (NexoRutaApiException) { }
        catch (HttpRequestException) { }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { }
        return null;
    }
}
