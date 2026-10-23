using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.Admin.Core.Security;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Auth.Clients;
using NexoRuta.ApiClient.Features.Auth.Contracts.Requests;
using NexoRuta.ApiClient.Features.Usuarios.Clients;

namespace NexoRuta.Admin.Features.Sesion.Pages;

public sealed class IngresarModel(AuthApiClient auth, UsuariosApiClient users) : PageModel
{
    [BindProperty, Required(ErrorMessage = "Ingresá un correo electrónico."), EmailAddress(ErrorMessage = "Ingresá un correo electrónico válido."), StringLength(254, ErrorMessage = "El correo no puede superar los 254 caracteres.")]
    public string Email { get; set; } = "";
    [BindProperty, Required(ErrorMessage = "Ingresá tu contraseña.")]
    public string Password { get; set; } = "";
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(bool expirada = false)
    {
        if (!expirada) return;
        await HttpContext.SignOutAsync();
        Error = "Tu sesión expiró o fue revocada. Ingresá nuevamente.";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync();
        try
        {
            if (!ModelState.IsValid) { Error = "Ingresá un correo válido y tu contraseña."; return Page(); }
            var login = await auth.LoginAsync(new LoginRequest(Email.Trim(), Password), HttpContext.RequestAborted);
            if (string.IsNullOrWhiteSpace(login.AccessToken) || !string.Equals(login.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
                throw new NexoRutaApiException("La API devolvió una sesión no compatible.", HttpStatusCode.BadGateway);
            if (login.Contexto != SesionUsuario.Plataforma)
            {
                Error = "Esta cuenta no tiene acceso a la administración de plataforma.";
                Password = "";
                ModelState.Remove(nameof(Password));
                return Page();
            }
            var actor = await users.ObtenerUsuarioActualAsync(login.AccessToken, HttpContext.RequestAborted);
            if (actor.Tipo != SesionUsuario.Plataforma || actor.OperadorId is not null || actor.ComercioId is not null ||
                actor.AccesoId == Guid.Empty || actor.UsuarioId == Guid.Empty)
            {
                Error = "Esta cuenta no tiene acceso a la administración de plataforma.";
                return Page();
            }
            var permissions = await users.ObtenerPermisosActualesAsync(login.AccessToken, HttpContext.RequestAborted);
            if (!permissions.Contains(AdminAccess.Permission, StringComparer.Ordinal))
            {
                Error = "Esta cuenta no tiene permisos para administrar la plataforma.";
                return Page();
            }
            var expiry = new DateTimeOffset(DateTime.SpecifyKind(login.ExpiresAtUtc, DateTimeKind.Utc));
            if (expiry <= DateTimeOffset.UtcNow) { Error = "La sesión expiró. Ingresá nuevamente."; return Page(); }
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                SesionUsuario.CrearPrincipal(actor, login.AccessToken, CookieAuthenticationDefaults.AuthenticationScheme),
                new AuthenticationProperties { IsPersistent = false, AllowRefresh = false, ExpiresUtc = expiry });
            return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
        }
        catch (NexoRutaApiException error) when (error.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest)
        { Error = "El correo o la contraseña no son correctos."; return Page(); }
        catch (NexoRutaApiException error) when (error.StatusCode == HttpStatusCode.Forbidden)
        { Error = "Esta cuenta no tiene acceso a la administración de plataforma."; return Page(); }
        catch (Exception error) when (error is NexoRutaApiException or HttpRequestException or JsonException ||
                                    error is OperationCanceledException && !HttpContext.RequestAborted.IsCancellationRequested)
        {
            Response.StatusCode = 503;
            Error = "No se pudo iniciar sesión. Intentá nuevamente más tarde.";
            return Page();
        }
        finally
        {
            Password = "";
            ModelState.Remove(nameof(Password));
        }
    }
}
