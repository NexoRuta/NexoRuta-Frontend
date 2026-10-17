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
        catch (NexoRutaApiException error) when (error.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect("/ingresar?expirada=true");
        }
        catch (NexoRutaApiException) { Error = "No pudimos confirmar el estado. Cerrá sesión y volvé a ingresar más tarde."; }
        catch (InvalidOperationException)
        { await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); return Redirect("/ingresar?expirada=true"); }
        catch (HttpRequestException) { Error = "El estado no está disponible porque la API no responde."; }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        { Error = "La consulta tardó demasiado. Intentá nuevamente."; }
        return Page();
    }
}
