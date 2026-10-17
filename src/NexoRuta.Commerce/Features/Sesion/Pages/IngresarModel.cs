using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Auth.Clients;
using NexoRuta.ApiClient.Features.Auth.Contracts.Requests;
using NexoRuta.ApiClient.Features.Usuarios.Clients;

namespace NexoRuta.Commerce.Features.Sesion.Pages;

[AllowAnonymous]
public sealed class IngresarModel(AuthApiClient authClient, UsuariosApiClient usuariosClient) : PageModel
{
    public string? Error { get; private set; }

    [BindProperty, Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";

    [BindProperty, Required]
    public string Password { get; set; } = "";

    public async Task OnGetAsync(bool expirada = false)
    {
        if (!expirada) return;
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        Error = "Tu sesión expiró o fue revocada. Ingresá nuevamente.";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            Error = "Ingresá un correo válido y tu contraseña.";
            Password = "";
            ModelState.Remove(nameof(Password));
            return Page();
        }

        try
        {
            var login = await authClient.LoginAsync(new LoginRequest(Email.Trim(), Password), HttpContext.RequestAborted);
            if (!string.Equals(login.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
                throw new NexoRutaApiException("La API devolvió un tipo de sesión no compatible.", System.Net.HttpStatusCode.BadGateway);

            if (login.Contexto == SesionUsuario.OrganizacionSuspendida)
            {
                if (login.TipoOrganizacion != "Comercio")
                {
                    Error = "Esta cuenta no tiene acceso a Commerce.";
                    return LimpiarPassword();
                }
                var restrictedExpiry = new DateTimeOffset(DateTime.SpecifyKind(login.ExpiresAtUtc, DateTimeKind.Utc));
                if (restrictedExpiry <= DateTimeOffset.UtcNow)
                    throw new NexoRutaApiException("La sesiÃ³n devuelta ya expirÃ³. IngresÃ¡ nuevamente.", System.Net.HttpStatusCode.Unauthorized);
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                    SesionUsuario.CrearPrincipalSuspendido(login.AccessToken, CookieAuthenticationDefaults.AuthenticationScheme),
                    new AuthenticationProperties { IsPersistent = false, AllowRefresh = false, ExpiresUtc = restrictedExpiry });
                return LocalRedirect("/organizacion-suspendida");
            }

            var usuario = await usuariosClient.ObtenerUsuarioActualAsync(login.AccessToken, HttpContext.RequestAborted);
            if (usuario.Tipo is not (SesionUsuario.Comercio or SesionUsuario.SinOrganizacion))
                Error = "Esta cuenta no tiene acceso a Commerce.";
            else
            {
                var expiry = new DateTimeOffset(DateTime.SpecifyKind(login.ExpiresAtUtc, DateTimeKind.Utc));
                if (expiry <= DateTimeOffset.UtcNow)
                    throw new NexoRutaApiException("La sesión devuelta ya expiró. Ingresá nuevamente.", System.Net.HttpStatusCode.Unauthorized);

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    SesionUsuario.CrearPrincipal(usuario, login.AccessToken, CookieAuthenticationDefaults.AuthenticationScheme),
                    new AuthenticationProperties { IsPersistent = false, AllowRefresh = false, ExpiresUtc = expiry });
                return LocalRedirect(usuario.Tipo == SesionUsuario.SinOrganizacion ? "/organizacion" : "/envios");
            }
        }
        catch (NexoRutaApiException exception) { Error = exception.Message; }
        catch (HttpRequestException) { Error = "No se pudo conectar con la API de NexoRuta."; }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { Error = "La API no respondió a tiempo. Intentá nuevamente."; }

        return LimpiarPassword();
    }

    private IActionResult LimpiarPassword()
    {
        Password = "";
        ModelState.Remove(nameof(Password));
        return Page();
    }
}
