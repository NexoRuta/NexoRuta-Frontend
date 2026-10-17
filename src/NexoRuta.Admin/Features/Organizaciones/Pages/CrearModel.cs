using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.Admin.Core.Security;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Plataforma.Clients;
using NexoRuta.ApiClient.Features.Plataforma.Contracts.Requests;

namespace NexoRuta.Admin.Features.Organizaciones.Pages;

public sealed class CrearModel(OrganizacionesAdminApiClient organizations) : PageModel
{
    public string Tipo => (string)RouteData.Values["tipo"]!;
    public string Titulo => Tipo == "operadores" ? "operator" : "commerce";
    [BindProperty, Required, StringLength(160)]
    public string Nombre { get; set; } = "";
    [BindProperty, Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";
    [BindProperty, Required, MinLength(12)]
    public string Password { get; set; } = "";
    [BindProperty, Required, Compare(nameof(Password), ErrorMessage = "Passwords must match.")]
    public string ConfirmPassword { get; set; } = "";
    public string? Error { get; private set; }
    [TempData]
    public string? Confirmacion { get; set; }
    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            if (!ModelState.IsValid) return Page();
            var result = await organizations.CrearAsync(Tipo, new CrearOrganizacionRequest
            {
                Nombre = Nombre.Trim(), Administrador = new PrimerAdministradorRequest { Email = Email.Trim(), Password = Password }
            }, SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            Confirmacion = "Organization and its first administrator were created. No administrator session was started.";
            return LocalRedirect($"/{Tipo}/{result.Organizacion.Id}");
        }
        catch (NexoRutaApiException error)
        {
            if (await ApiSession.RedirectAsync(this, error) is { } redirect) return redirect;
            Error = error.StatusCode switch
            {
                HttpStatusCode.BadRequest => "Review the name, email and password requirements.",
                HttpStatusCode.Conflict => "The email is already registered or the request conflicts with existing data.",
                _ => "The API is unavailable. Check the organization list before retrying: the request may have completed."
            };
            if (error.StatusCode is not (HttpStatusCode.BadRequest or HttpStatusCode.Conflict)) Response.StatusCode = 503;
            return Page();
        }
        catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !HttpContext.RequestAborted.IsCancellationRequested)
        {
            Response.StatusCode = 503;
            Error = "The API is unavailable. Check the organization list before retrying: the request may have completed.";
            return Page();
        }
        finally
        {
            Password = ConfirmPassword = "";
            // Clear attempted values, not validation errors: the user still needs to know why confirmation failed.
            ModelState.SetModelValue(nameof(Password), null, null);
            ModelState.SetModelValue(nameof(ConfirmPassword), null, null);
        }
    }
}
