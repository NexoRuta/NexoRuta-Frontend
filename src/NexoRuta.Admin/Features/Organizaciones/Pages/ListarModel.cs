using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.Admin.Core.Security;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Plataforma.Clients;
using NexoRuta.ApiClient.Features.Plataforma.Contracts.Responses;

namespace NexoRuta.Admin.Features.Organizaciones.Pages;

public sealed class ListarModel(OrganizacionesAdminApiClient organizations) : PageModel
{
    public string Tipo => (string)RouteData.Values["tipo"]!;
    public string Titulo => Tipo == "operadores" ? "Operators" : "Commerces";
    [BindProperty(SupportsGet = true), Range(1, int.MaxValue)]
    public int Pagina { get; set; } = 1;
    [BindProperty(SupportsGet = true), Range(1, 100)]
    public int TamanoPagina { get; set; } = 20;
    public PaginaOrganizacionesResponse? Resultado { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!ModelState.IsValid) { Response.StatusCode = 400; Error = "Invalid pagination. Page size must be between 1 and 100."; return Page(); }
        try
        {
            Resultado = await organizations.ListarAsync(Tipo, Pagina, TamanoPagina, SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            return Page();
        }
        catch (NexoRutaApiException error)
        {
            if (await ApiSession.RedirectAsync(this, error) is { } redirect) return redirect;
            if (error.StatusCode == System.Net.HttpStatusCode.BadRequest)
            { Response.StatusCode = 400; Error = "Invalid pagination."; return Page(); }
            return Redirect("/api-indisponible");
        }
        catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !HttpContext.RequestAborted.IsCancellationRequested)
        { return Redirect("/api-indisponible"); }
    }
}
