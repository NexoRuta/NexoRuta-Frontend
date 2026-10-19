using Microsoft.AspNetCore.Mvc;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Tarifas.Clients;
using NexoRuta.Backoffice.Features.Tarifas;

namespace NexoRuta.Backoffice.Features.Tarifas.Pages;

public sealed class PublicacionesIndexModel(TarifasApiClient api) : TarifasPageModel
{
    [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int TamanoPagina { get; set; } = 20;
    public NexoRuta.ApiClient.Features.Tarifas.Contracts.PaginaPublicacionesTarifarioResponse? Resultado { get; private set; }
    public Guid? VersionVigenteId { get; private set; }
    public int Paginas => Resultado is null ? 1 : Math.Max(1, (int)Math.Ceiling(Resultado.Total / (double)TamanoPagina));

    public async Task<IActionResult> OnGetAsync()
    {
        if (Pagina is < 1 or > 10_000) { Pagina = 1; Error = "La página solicitada no es válida."; }
        if (TamanoPagina is not (10 or 20 or 50 or 100)) { TamanoPagina = 20; Error ??= "El tamaño de página solicitado no es válido."; }
        try
        {
            var token = SesionUsuario.ObtenerToken(User);
            var state = await api.ObtenerEstadoAsync(token, HttpContext.RequestAborted);
            VersionVigenteId = state.PublicacionVigente?.VersionTarifarioId;
            Resultado = await api.ListarPublicacionesAsync(token, Pagina, TamanoPagina, HttpContext.RequestAborted);
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
}
