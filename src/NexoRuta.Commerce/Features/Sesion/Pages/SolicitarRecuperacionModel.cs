using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Features.Auth.Clients;
using NexoRuta.ApiClient.Features.Auth.Contracts.Requests;

namespace NexoRuta.Commerce.Features.Sesion.Pages;

[AllowAnonymous]
public sealed class SolicitarRecuperacionModel(AuthApiClient auth) : PageModel
{
    [BindProperty, Required(ErrorMessage = "Ingresá un correo electrónico."), EmailAddress(ErrorMessage = "Ingresá un correo electrónico válido."), StringLength(254, ErrorMessage = "El correo no puede superar los 254 caracteres.")]
    public string Email { get; set; } = "";

    public bool SolicitudEnviada { get; private set; }
    public string? Error { get; private set; }

    public void OnGet() => Headers();

    public async Task<IActionResult> OnPostAsync()
    {
        Headers();
        if (!ModelState.IsValid)
        {
            Error = "Ingresá un correo electrónico válido.";
            Email = "";
            ModelState.Remove(nameof(Email));
            return Page();
        }

        try
        {
            await auth.SolicitarRecuperacionAsync(new SolicitarRecuperacionPasswordRequest(Email.Trim()), HttpContext.RequestAborted);
            SolicitudEnviada = true;
            Email = "";
            ModelState.Remove(nameof(Email));
        }
        catch (NexoRutaApiException error) { Error = error.Message; }
        catch (HttpRequestException) { Error = "No se pudo conectar con la API. Intentá nuevamente más tarde."; }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        { Error = "La API no respondió a tiempo. Intentá nuevamente más tarde."; }

        return Page();
    }

    private void Headers()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
