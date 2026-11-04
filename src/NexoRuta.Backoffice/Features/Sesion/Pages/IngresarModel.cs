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

namespace NexoRuta.Backoffice.Features.Sesion.Pages;

[AllowAnonymous]
public sealed class IngresarModel(AuthApiClient authClient, UsuariosApiClient usuariosClient, ILogger<IngresarModel> logger) : PageModel
{
    public string? Error { get; private set; }
    public bool ApiNoDisponible { get; private set; }
    [BindProperty(SupportsGet=true)] public string? ReturnUrl { get; set; }
    [BindProperty(SupportsGet=true,Name="rechazo")] public string? IngresoRechazado { get; set; }
    public string? DestinoRetorno => RutaSesion.Destino(ReturnUrl);

    [BindProperty, Required(ErrorMessage = "Ingresá un correo electrónico."), EmailAddress(ErrorMessage = "Ingresá un correo electrónico válido."), StringLength(254, ErrorMessage = "El correo no puede superar los 254 caracteres.")]
    public string Email { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Ingresá tu contraseña.")]
    public string Password { get; set; } = "";

    public async Task<IActionResult> OnGetAsync(bool expirada = false)
    {
        if (!expirada) return Page();
        Error = "Tu sesión ya no es válida. Ingresá nuevamente.";
        // A delayed 401 belongs to its old request. It must never delete a newer login cookie.
        if (Guid.TryParseExact(IngresoRechazado,"N",out var rejected)
            && SesionUsuario.IngresoWeb(User) is {} current && current != rejected.ToString("N")
            && User.Identity?.IsAuthenticated == true && User.HasClaim(SesionUsuario.ClaimTipoAcceso, SesionUsuario.Operador))
        {
            try
            {
                var actual=await usuariosClient.ObtenerUsuarioActualAsync(SesionUsuario.ObtenerToken(User),HttpContext.RequestAborted);
                if (actual.Tipo == SesionUsuario.Operador) return LocalRedirect(DestinoRetorno ?? "/");
                Error="Esta cuenta no tiene acceso a este portal.";
            }
            catch(NexoRutaApiException error) when(error.StatusCode==System.Net.HttpStatusCode.Unauthorized)
            { logger.LogInformation("Session recovery rejected. Status={Status} AuthTrace={AuthTrace}",401,error.AuthTraceId); }
            catch(NexoRutaApiException error) when(error.StatusCode==System.Net.HttpStatusCode.Forbidden)
            { Error="No tenés permiso para abrir este portal. Tu sesión no se cerró."; }
            catch(NexoRutaApiException) { ApiNoDisponible=true; }
            catch(HttpRequestException) { ApiNoDisponible=true; }
            catch(OperationCanceledException) when(!HttpContext.RequestAborted.IsCancellationRequested) { ApiNoDisponible=true; }
            catch(InvalidOperationException) { /* Ticket without a usable API token: require explicit sign-in. */ }
        }
        if(ApiNoDisponible) Error="No pudimos verificar tu sesión porque la API no está disponible. Conservamos la sesión; volvé a intentarlo.";
        return Page();
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

        var credencialesVerificadas = false;
        try
        {
            var login = await authClient.LoginAsync(new LoginRequest(Email.Trim(), Password), HttpContext.RequestAborted);
            credencialesVerificadas = true;
            if (!string.Equals(login.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
                throw new NexoRutaApiException("La API devolvió un tipo de sesión no compatible.", System.Net.HttpStatusCode.BadGateway);

            if (login.Contexto == SesionUsuario.OrganizacionSuspendida)
            {
                if (login.TipoOrganizacion != "Operador")
                {
                    Error = "Esta cuenta no tiene acceso a Backoffice.";
                    return LimpiarPassword();
                }
                var restrictedExpiry = new DateTimeOffset(DateTime.SpecifyKind(login.ExpiresAtUtc, DateTimeKind.Utc));
                if (restrictedExpiry <= DateTimeOffset.UtcNow)
                    throw new NexoRutaApiException("La sesión devuelta ya expiró. Ingresá nuevamente.", System.Net.HttpStatusCode.Unauthorized);
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                    SesionUsuario.CrearPrincipalSuspendido(login.AccessToken, CookieAuthenticationDefaults.AuthenticationScheme),
                    new AuthenticationProperties { IsPersistent = false, AllowRefresh = false, ExpiresUtc = restrictedExpiry });
                return LocalRedirect("/OrganizacionSuspendida");
            }

            var usuario = await usuariosClient.ObtenerUsuarioActualAsync(login.AccessToken, HttpContext.RequestAborted);
            if (usuario.Tipo != SesionUsuario.Operador)
                Error = "Esta cuenta no tiene acceso al Backoffice.";
            else
            {
                var expiry = new DateTimeOffset(DateTime.SpecifyKind(login.ExpiresAtUtc, DateTimeKind.Utc));
                if (expiry <= DateTimeOffset.UtcNow)
                    throw new NexoRutaApiException("La sesión devuelta ya expiró. Ingresá nuevamente.", System.Net.HttpStatusCode.Unauthorized);

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    SesionUsuario.CrearPrincipal(usuario, login.AccessToken, CookieAuthenticationDefaults.AuthenticationScheme),
                    new AuthenticationProperties { IsPersistent = false, AllowRefresh = false, ExpiresUtc = expiry });
                return LocalRedirect(DestinoRetorno ?? "/");
            }
        }
        catch (NexoRutaApiException exception)
        {
            logger.LogInformation("Sign-in could not complete. CredentialsVerified={CredentialsVerified} Status={Status} AuthTrace={AuthTrace}",credencialesVerificadas,(int?)exception.StatusCode,exception.AuthTraceId);
            Error = exception.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.BadRequest
                ? credencialesVerificadas ? "No pudimos validar la sesión recién iniciada. Volvé a intentar el ingreso." : "El correo o la contraseña no son correctos."
                : "No se pudo iniciar sesión. Intentá nuevamente.";
        }
        catch (HttpRequestException) { Error = "No se pudo conectar con la API de NexoRuta. Tu sesión no se cerró."; }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { Error = "La API no respondió a tiempo. Intentá nuevamente; tu sesión no se cerró."; }

        return LimpiarPassword();
    }

    private IActionResult LimpiarPassword()
    {
        Password = "";
        ModelState.Remove(nameof(Password));
        return Page();
    }
}
