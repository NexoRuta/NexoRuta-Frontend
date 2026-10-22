using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Organizaciones.Clients;
using NexoRuta.ApiClient.Features.Organizaciones.Contracts.Responses;
using System.ComponentModel.DataAnnotations;
using System.Net;

namespace NexoRuta.Backoffice.Features.Sesion.Pages;

[AllowAnonymous]
public sealed class OrganizacionSuspendidaModel(OrganizacionSuspendidaApiClient client) : PageModel
{
    public OrganizacionSuspendidaResponse? Estado { get; private set; }
    public IReadOnlyList<SolicitudReactivacionResponse> Solicitudes { get; private set; } = [];
    [BindProperty, Required(ErrorMessage = "Ingresá un motivo."), StringLength(2000, ErrorMessage = "El motivo no puede superar los 2000 caracteres.")]
    public string Motivo { get; set; } = "";
    [BindProperty] public bool Confirmar { get; set; }
    [TempData] public string? Confirmacion { get; set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated != true ||
            !User.HasClaim(SesionUsuario.ClaimTipoAcceso, SesionUsuario.OrganizacionSuspendida))
            return Redirect("/ingresar");
        try
        {
            var token = SesionUsuario.ObtenerToken(User);
            Estado = await client.ObtenerAsync(token, HttpContext.RequestAborted);
            if (Estado.EsPropietario)
                Solicitudes = await client.ListarReactivacionesAsync(token, HttpContext.RequestAborted);
        }
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

    public async Task<IActionResult> OnPostAsync()
    {
        if (!Confirmar) ModelState.AddModelError(nameof(Confirmar), "Confirmá que querés solicitar la reactivación.");
        if (!ModelState.IsValid)
        {
            await OnGetAsync();
            return Page();
        }
        try
        {
            await client.SolicitarReactivacionAsync(Motivo.Trim(), SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            Confirmacion = "La solicitud quedó pendiente. El operador sigue suspendido hasta la resolución de plataforma.";
            return RedirectToPage();
        }
        catch (NexoRutaApiException error) when (error.StatusCode == HttpStatusCode.Unauthorized)
        { await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); return Redirect("/ingresar?expirada=true"); }
        catch (NexoRutaApiException error)
        {
            Error = error.StatusCode switch
            {
                HttpStatusCode.Forbidden => "Solo el dueño registrado puede solicitar la reactivación.",
                HttpStatusCode.Conflict => "Ya existe una solicitud pendiente o la organización está activa. Actualizá la página.",
                HttpStatusCode.BadRequest => error.Message,
                _ => "No se pudo enviar la solicitud. Intentá nuevamente."
            };
        }
        catch (HttpRequestException) { Error = "La API no está disponible. Intentá nuevamente."; }
        await OnGetAsync();
        return Page();
    }
}
