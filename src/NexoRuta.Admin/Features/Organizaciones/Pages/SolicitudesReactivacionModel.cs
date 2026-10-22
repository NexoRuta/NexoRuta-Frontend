using System.Net;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.Admin.Core.Security;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Organizaciones.Contracts.Responses;
using NexoRuta.ApiClient.Features.Plataforma.Clients;
using NexoRuta.ApiClient.Features.Plataforma.Contracts.Responses;

namespace NexoRuta.Admin.Features.Organizaciones.Pages;

public sealed class SolicitudesReactivacionModel(OrganizacionesAdminApiClient organizations) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Estado { get; set; }
    [BindProperty(SupportsGet = true)] public string? Tipo { get; set; }
    [BindProperty(SupportsGet = true), Range(1, int.MaxValue)] public int Pagina { get; set; } = 1;
    [BindProperty(SupportsGet = true), Range(1, 100)] public int TamanoPagina { get; set; } = 20;
    public PaginaSolicitudesReactivacionResponse? Resultado { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!ModelState.IsValid || Estado is not (null or "Pendiente" or "Aprobada" or "Rechazada" or "AtendidaPorSoporte") ||
            Tipo is not (null or "Operador" or "Comercio"))
        { Response.StatusCode = 400; Error = "Elegí filtros y paginación válidos."; return Page(); }
        try
        {
            Resultado = await organizations.ListarSolicitudesReactivacionAsync(Estado, Tipo, Pagina, TamanoPagina,
                SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            return Page();
        }
        catch (NexoRutaApiException error)
        {
            if (await ApiSession.RedirectAsync(this, error) is { } redirect) return redirect;
            Response.StatusCode = error.StatusCode == HttpStatusCode.BadRequest ? 400 : 503;
            Error = error.StatusCode == HttpStatusCode.BadRequest ? error.Message : "No se pudieron cargar las solicitudes.";
            return Page();
        }
        catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !HttpContext.RequestAborted.IsCancellationRequested)
        { return Redirect("/api-indisponible"); }
    }
}
