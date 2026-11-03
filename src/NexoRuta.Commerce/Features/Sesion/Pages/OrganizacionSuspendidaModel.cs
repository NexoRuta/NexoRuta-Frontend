using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Organizaciones.Clients;
using NexoRuta.ApiClient.Features.Organizaciones.Contracts.Responses;

namespace NexoRuta.Commerce.Features.Sesion.Pages;

[AllowAnonymous]
public sealed class OrganizacionSuspendidaModel(OrganizacionSuspendidaApiClient client) : PageModel
{
    public OrganizacionSuspendidaResponse? Estado { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated != true ||
            !User.HasClaim(SesionUsuario.ClaimTipoAcceso, SesionUsuario.OrganizacionSuspendida))
            return Redirect("/ingresar");
        try { Estado = await client.ObtenerAsync(SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted); }
        catch (NexoRutaApiException error) when (error.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return Redirect(RutaSesion.Ingreso(Request.Path,true,SesionUsuario.IngresoWeb(User)));
        }
        catch (NexoRutaApiException) { Error = "No pudimos confirmar el estado. Revisá tus permisos o reintentá cuando la API esté disponible; tu sesión no se cerró."; }
        catch (InvalidOperationException)
        { return Redirect(RutaSesion.Ingreso(Request.Path,true,SesionUsuario.IngresoWeb(User))); }
        catch (HttpRequestException) { Error = "El estado no está disponible porque la API no responde."; }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        { Error = "La consulta tardó demasiado. Intentá nuevamente."; }
        return Page();
    }
}
