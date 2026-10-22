using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Auth.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Clients;

namespace NexoRuta.Commerce.Features.Sesion.Pages;

[Authorize]
public sealed class OrganizacionModel(AuthApiClient auth, UsuariosApiClient usuarios) : PageModel
{
    [BindProperty, Required, RegularExpression("^(comercio|operador)$")]
    public string Tipo { get; set; } = "comercio";
    [BindProperty, Required, StringLength(160)]
    public string Nombre { get; set; } = "";
    public string? Error { get; private set; }
    public bool PuedeCrear { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await Procesar(false);
    public async Task<IActionResult> OnPostAsync() => await Procesar(true);

    private async Task<IActionResult> Procesar(bool crear)
    {
        try
        {
            var token = SesionUsuario.ObtenerToken(User);
            var actual = await usuarios.ObtenerUsuarioActualAsync(token, HttpContext.RequestAborted);
            if (actual.Tipo != SesionUsuario.SinOrganizacion)
                return actual.Tipo == SesionUsuario.Comercio ? LocalRedirect("/envios") : Forbid();
            PuedeCrear = true;
            if (!crear) return Page();
            if (!ModelState.IsValid) { Error = "Elegí comercio u operador e ingresá un nombre de hasta 160 caracteres."; return Page(); }
            await auth.CrearOrganizacionPropiaAsync(Tipo, Nombre.Trim(), token, HttpContext.RequestAborted);
            // The old identity-only token is now invalid. No implicit refresh or cross-application SSO.
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return LocalRedirect($"/organizacion-creada/{Tipo}");
        }
        catch (NexoRutaApiException error)
        {
            if (error.StatusCode == HttpStatusCode.Unauthorized)
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return LocalRedirect("/ingresar?expirada=true");
            }
            if (error.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Forbidden) PuedeCrear = false;
            Error = error.StatusCode == HttpStatusCode.Conflict
                ? "La identidad ya tiene una organización. Ingresá nuevamente en el portal correspondiente." : error.Message;
        }
        catch (HttpRequestException) { Error = "No se pudo conectar con la API de NexoRuta. Intentá nuevamente."; }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { Error = "La API no respondió a tiempo. Intentá nuevamente."; }
        return Page();
    }
}
