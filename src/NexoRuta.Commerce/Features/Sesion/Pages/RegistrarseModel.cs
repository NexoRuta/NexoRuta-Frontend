using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Features.Auth.Clients;
using NexoRuta.ApiClient.Features.Auth.Contracts.Requests;

namespace NexoRuta.Commerce.Features.Sesion.Pages;

[AllowAnonymous]
public sealed class RegistrarseModel(AuthApiClient auth) : PageModel
{
    [BindProperty, Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";
    [BindProperty, Required, MinLength(12)]
    public string Password { get; set; } = "";
    [BindProperty, Required, Compare(nameof(Password))]
    public string ConfirmPassword { get; set; } = "";
    public string? Error { get; private set; }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            if (!ModelState.IsValid) Error = "Ingresá un correo válido y una contraseña de al menos 12 caracteres; la confirmación debe coincidir.";
            else
            {
                await auth.RegistrarAsync(new RegistroRequest(Email.Trim(), Password), HttpContext.RequestAborted);
                return LocalRedirect("/ingresar?registrado=true");
            }
        }
        catch (NexoRutaApiException error) { Error = error.Message; }
        catch (HttpRequestException) { Error = "No se pudo conectar con la API de NexoRuta. Intentá nuevamente."; }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { Error = "La API no respondió a tiempo. Intentá nuevamente."; }
        Password = ConfirmPassword = "";
        ModelState.Remove(nameof(Password));
        ModelState.Remove(nameof(ConfirmPassword));
        return Page();
    }
}
