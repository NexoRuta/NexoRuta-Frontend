using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.Admin.Core.Security;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Plataforma.Clients;
using NexoRuta.ApiClient.Features.Plataforma.Contracts.Requests;
using NexoRuta.ApiClient.Features.Plataforma.Contracts.Responses;

namespace NexoRuta.Admin.Features.Organizaciones.Pages;

public sealed class SolicitudBajaDetalleModel(OrganizacionesAdminApiClient organizations) : PageModel
{
    public SolicitudBajaAdminResponse? Solicitud { get; private set; }
    [BindProperty, Required(ErrorMessage = "Ingresá un motivo."), StringLength(2000, ErrorMessage = "El motivo no puede superar los 2000 caracteres.")]
    public string Motivo { get; set; } = "";
    [BindProperty, Required(ErrorMessage = "Falta la versión de la solicitud."), Range(0, long.MaxValue, ErrorMessage = "La versión de la solicitud no es válida.")]
    public long? Version { get; set; }
    [BindProperty] public bool Confirmar { get; set; }
    [BindProperty] public string? Decision { get; set; }
    public string? Error { get; private set; }
    [TempData] public string? Confirmacion { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id) => await CargarAsync(id) ?? Page();

    public async Task<IActionResult> OnPostResolverAsync(Guid id)
    {
        if (Decision is not ("Aprobar" or "Rechazar"))
            ModelState.AddModelError(nameof(Decision), "Elegí aprobar o rechazar la solicitud.");
        if (!Confirmar)
            ModelState.AddModelError(nameof(Confirmar), "Confirmá que entendés el efecto de esta decisión.");
        if (!ModelState.IsValid)
        {
            await CargarAsync(id);
            return Page();
        }

        try
        {
            Solicitud = await organizations.ResolverSolicitudBajaAsync(id,
                new(Decision!, Motivo.Trim(), Version!.Value), SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            Confirmacion = Decision == "Aprobar"
                ? "La solicitud fue aprobada y la organización quedó suspendida."
                : "La solicitud fue rechazada; el estado de la organización no cambió.";
            return Redirect($"/solicitudes-baja/{id}");
        }
        catch (NexoRutaApiException error)
        {
            if (await ApiSession.RedirectAsync(this, error) is { } redirect) return redirect;
            Error = error.StatusCode switch
            {
                HttpStatusCode.BadRequest => "No se pudo resolver la solicitud. Revisá los campos indicados.",
                HttpStatusCode.NotFound => "No se encontró la solicitud.",
                HttpStatusCode.Conflict => "La solicitud cambió o ya fue resuelta. Actualizá la página antes de volver a intentarlo.",
                _ => "No se pudo guardar la resolución porque la API no está disponible."
            };
            await CargarAsync(id);
            return Page();
        }
        catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !HttpContext.RequestAborted.IsCancellationRequested)
        { Error = "La API no está disponible. Intentá nuevamente más tarde."; await CargarAsync(id); return Page(); }
    }

    private async Task<IActionResult?> CargarAsync(Guid id)
    {
        try
        {
            Solicitud = await organizations.ObtenerSolicitudBajaAsync(id, SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            Version ??= Solicitud.Version;
        }
        catch (NexoRutaApiException error)
        {
            if (await ApiSession.RedirectAsync(this, error) is { } redirect) return redirect;
            if (error.StatusCode == HttpStatusCode.NotFound) { Response.StatusCode = 404; Error = "No se encontró la solicitud."; }
            else return Redirect("/api-indisponible");
        }
        catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !HttpContext.RequestAborted.IsCancellationRequested)
        { return Redirect("/api-indisponible"); }
        return null;
    }
}
