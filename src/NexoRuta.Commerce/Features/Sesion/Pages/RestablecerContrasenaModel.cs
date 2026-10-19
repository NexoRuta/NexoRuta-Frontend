using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Features.Auth.Clients;
using NexoRuta.ApiClient.Features.Auth.Contracts.Requests;

namespace NexoRuta.Commerce.Features.Sesion.Pages;

[AllowAnonymous]
public sealed class RestablecerContrasenaModel(AuthApiClient auth, IDataProtectionProvider dataProtection) : PageModel
{
    private const string CookieName = "NexoRuta.PasswordRecovery";
    private const string CookiePath = "/restablecer-contrasena";
    private readonly IDataProtector protector = dataProtection.CreateProtector("NexoRuta.PasswordRecovery.Cookie.v1");

    [BindProperty, Required, StringLength(1024, MinimumLength = 1)]
    public string NuevaPassword { get; set; } = "";

    public bool PuedeRestablecer { get; private set; }
    public bool Guardada { get; private set; }
    public string? Error { get; private set; }

    public IActionResult OnGet(Guid? userId, string? token, bool invalid = false)
    {
        Headers();
        if (userId.HasValue || token is not null)
        {
            if (userId is not Guid id || id == Guid.Empty || string.IsNullOrWhiteSpace(token) || token.Length > 4096)
                return RedirectToPage(new { invalid = true });

            Response.Cookies.Append(CookieName,
                protector.Protect(JsonSerializer.Serialize(new ResetLink(id, token))), CookieOptions());
            return RedirectToPage();
        }

        PuedeRestablecer = !invalid && Request.Cookies.ContainsKey(CookieName);
        if (!PuedeRestablecer)
            Error = "El enlace no es válido o venció. Solicitá uno nuevo.";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Headers();
        PuedeRestablecer = true;
        if (!ModelState.IsValid)
        {
            Error = "La contraseña nueva no cumple la política de seguridad. Usá al menos 12 caracteres.";
            NuevaPassword = "";
            ModelState.Remove(nameof(NuevaPassword));
            return Page();
        }

        try
        {
            if (!Request.Cookies.TryGetValue(CookieName, out var protectedLink))
                return InvalidLink();
            var link = JsonSerializer.Deserialize<ResetLink>(protector.Unprotect(protectedLink));
            if (link is null || link.UserId == Guid.Empty || string.IsNullOrEmpty(link.Token))
                return InvalidLink();

            await auth.RestablecerPasswordAsync(
                new RestablecerPasswordRequest(link.UserId, link.Token, NuevaPassword), HttpContext.RequestAborted);
            Response.Cookies.Delete(CookieName, new CookieOptions { Path = CookiePath });
            Guardada = true;
        }
        catch (NexoRutaApiException error) { Error = error.Message; }
        catch (CryptographicException) { return InvalidLink(); }
        catch (JsonException) { return InvalidLink(); }
        catch (HttpRequestException) { Error = "No se pudo conectar con la API. Intentá nuevamente más tarde."; }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        { Error = "La API no respondió a tiempo. Intentá nuevamente más tarde."; }
        finally
        {
            NuevaPassword = "";
            ModelState.Remove(nameof(NuevaPassword));
        }

        return Page();
    }

    private IActionResult InvalidLink()
    {
        Response.Cookies.Delete(CookieName, new CookieOptions { Path = CookiePath });
        PuedeRestablecer = false;
        Error = "El enlace no es válido o venció. Solicitá uno nuevo.";
        return Page();
    }

    private CookieOptions CookieOptions() => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        IsEssential = true,
        Path = CookiePath,
        Expires = DateTimeOffset.UtcNow.AddMinutes(15),
        MaxAge = TimeSpan.FromMinutes(15)
    };

    private void Headers()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    private sealed record ResetLink(Guid UserId, string Token);
}
