using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.ZonasCobertura.Clients;
using NexoRuta.ApiClient.Features.ZonasCobertura.Contracts;
using NexoRuta.Backoffice.Core.Security;

namespace NexoRuta.Backoffice.Features.ZonasCobertura.Pages;

public sealed class IndexModel(ZonasCoberturaApiClient api) : PageModel
{
    public IReadOnlyList<ZonaCoberturaResumenResponse> Activas { get; private set; } = [];
    public IReadOnlyList<ZonaCoberturaResumenResponse> Inactivas { get; private set; } = [];
    public int Total { get; private set; }
    public int Paginas { get; private set; } = 1;
    public string? Error { get; private set; }

    [BindProperty(SupportsGet = true)] public string Estado { get; set; } = ZonaCoberturaUi.FiltroActivas;
    [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int TamanoPagina { get; set; } = 20;

    public async Task<IActionResult> OnGetAsync()
    {
        if (Estado is not (ZonaCoberturaUi.FiltroActivas or ZonaCoberturaUi.FiltroInactivas or ZonaCoberturaUi.FiltroTodas))
        {
            Estado = ZonaCoberturaUi.FiltroActivas;
            Error = "El filtro de estado no es válido.";
        }
        if (Pagina is < 1 or > 10_000) { Pagina = 1; Error ??= "La página solicitada no es válida."; }
        if (TamanoPagina is not (10 or 20 or 50)) { TamanoPagina = 20; Error ??= "El tamaño de página solicitado no es válido."; }

        try
        {
            var token = SesionUsuario.ObtenerToken(User);
            PaginaZonasCoberturaResponse? active = null;
            PaginaZonasCoberturaResponse? inactive = null;
            if (Estado is ZonaCoberturaUi.FiltroActivas or ZonaCoberturaUi.FiltroTodas)
                active = await api.ListarAsync(token, ZonaCoberturaUi.EstadoActiva, Pagina, TamanoPagina, HttpContext.RequestAborted);
            if (Estado is ZonaCoberturaUi.FiltroInactivas or ZonaCoberturaUi.FiltroTodas)
                inactive = await api.ListarAsync(token, ZonaCoberturaUi.EstadoInactiva, Pagina, TamanoPagina, HttpContext.RequestAborted);

            Activas = active?.Items ?? [];
            Inactivas = inactive?.Items ?? [];
            Total = (active?.Total ?? 0) + (inactive?.Total ?? 0);
            var largestGroupTotal = Math.Max(active?.Total ?? 0, inactive?.Total ?? 0);
            Paginas = Math.Max(1, (int)Math.Ceiling(largestGroupTotal / (double)TamanoPagina));
        }
        catch (NexoRutaApiException exception)
        {
            var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
            if (expired is not null) return expired;
            Error ??= ZonaCoberturaUi.ErrorMessage(exception);
        }
        catch (HttpRequestException) { Error ??= "La API de zonas no está disponible. Intentá nuevamente más tarde."; }

        return Page();
    }
}
