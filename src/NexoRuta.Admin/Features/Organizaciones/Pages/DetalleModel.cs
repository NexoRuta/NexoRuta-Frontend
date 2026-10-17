using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using NexoRuta.Admin.Core.Security;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Plataforma.Clients;
using NexoRuta.ApiClient.Features.Plataforma.Contracts.Responses;

namespace NexoRuta.Admin.Features.Organizaciones.Pages;

public sealed class DetalleModel(OrganizacionesAdminApiClient organizations) : PageModel
{
    public string Tipo => (string)RouteData.Values["tipo"]!;
    public string Titulo => Tipo == "operadores" ? "Operator" : "Commerce";
    public OrganizacionResponse? Organizacion { get; private set; }
    [BindProperty, Required, StringLength(2000)]
    public string Motivo { get; set; } = "";
    [BindProperty, Range(0, long.MaxValue)]
    public long Version { get; set; }
    [BindProperty]
    public bool Confirmar { get; set; }
    public string? Error { get; private set; }
    [TempData]
    public string? Confirmacion { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        if (!string.IsNullOrEmpty(Request.Query["actualizado"])) Confirmacion = "Organization state updated.";
        return await CargarAsync(id) ?? Page();
    }

    public async Task<IActionResult> OnPostCambiarEstadoAsync(Guid id, string estado)
    {
        if (estado is not ("Activa" or "Suspendida") || !Confirmar || !ModelState.IsValid)
        {
            Error = "Choose a state and provide a reason of up to 2000 characters.";
            await CargarAsync(id);
            return Page();
        }

        try
        {
            var token = SesionUsuario.ObtenerToken(User);
            await organizations.CambiarEstadoAsync(Tipo, id,
                new(estado, Motivo.Trim(), Version), token, HttpContext.RequestAborted);
            return Redirect($"/{Tipo}/{id}?actualizado=true");
        }
        catch (NexoRutaApiException error)
        {
            if (await ApiSession.RedirectAsync(this, error) is { } redirect) return redirect;
            if (error.StatusCode == System.Net.HttpStatusCode.NotFound) { Response.StatusCode = 404; Error = "Organization not found."; }
            else if (error.StatusCode == System.Net.HttpStatusCode.Conflict) Error = "The organization changed since it was loaded. Refresh and try again.";
            else Error = error.StatusCode == System.Net.HttpStatusCode.BadRequest ? error.Message : "The state could not be changed.";
            await CargarAsync(id);
            return Page();
        }
        catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !HttpContext.RequestAborted.IsCancellationRequested)
        { Error = "The API is unavailable. Try again later."; await CargarAsync(id); return Page(); }
    }

    private async Task<IActionResult?> CargarAsync(Guid id)
    {
        try
        {
            Organizacion = await organizations.ObtenerAsync(Tipo, id, SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            Version = Organizacion.Version;
        }
        catch (NexoRutaApiException error)
        {
            if (await ApiSession.RedirectAsync(this, error) is { } redirect) return redirect;
            if (error.StatusCode == System.Net.HttpStatusCode.NotFound) { Response.StatusCode = 404; Error ??= "Organization not found."; }
            else return Redirect("/api-indisponible");
        }
        catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !HttpContext.RequestAborted.IsCancellationRequested)
        { return Redirect("/api-indisponible"); }
        return null;
    }
}
