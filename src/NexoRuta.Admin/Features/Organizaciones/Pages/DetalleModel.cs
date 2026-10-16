using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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
    [TempData]
    public string? Confirmacion { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        try
        {
            Organizacion = await organizations.ObtenerAsync(Tipo, id, SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            return Page();
        }
        catch (NexoRutaApiException error)
        {
            if (await ApiSession.RedirectAsync(this, error) is { } redirect) return redirect;
            if (error.StatusCode == System.Net.HttpStatusCode.NotFound) { Response.StatusCode = 404; return Page(); }
            return Redirect("/api-indisponible");
        }
        catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !HttpContext.RequestAborted.IsCancellationRequested)
        { return Redirect("/api-indisponible"); }
    }
}
