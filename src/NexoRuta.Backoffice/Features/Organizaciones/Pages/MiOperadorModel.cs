using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Organizaciones.Clients;
using NexoRuta.ApiClient.Features.Organizaciones.Contracts.Requests;
using NexoRuta.ApiClient.Features.Organizaciones.Contracts.Responses;
using NexoRuta.Backoffice.Core.Security;

namespace NexoRuta.Backoffice.Features.Organizaciones.Pages;

public sealed class MiOperadorModel(OrganizacionPropiaApiClient apiClient) : PageModel
{
    public OrganizacionPropiaResponse? Organizacion { get; private set; }
    public string? Error { get; private set; }
    public string? Confirmacion { get; private set; }
    public IReadOnlyList<SolicitudBajaResponse> Solicitudes { get; private set; } = [];
    public bool MostrarFormularioNombre { get; private set; }

    [BindProperty, Required, StringLength(160)]
    public string Nombre { get; set; } = "";

    [BindProperty, Required, Range(0, long.MaxValue)]
    public long Version { get; set; }

    [BindProperty, StringLength(2000)]
    public string MotivoBaja { get; set; } = "";

    [BindProperty]
    public bool ConfirmarBaja { get; set; }

    public async Task<IActionResult> OnGetAsync() => await CargarAsync() ?? Page();

    public async Task<IActionResult> OnPostGuardarAsync()
    {
        if (string.IsNullOrWhiteSpace(Nombre) || Nombre.Trim().Length > 160 || Version < 0)
        {
            Error = "El nombre es obligatorio y no puede superar los 160 caracteres.";
            await CargarAsync(conservarEdicion: true);
            return Page();
        }

        try
        {
            Organizacion = await apiClient.ActualizarNombreAsync(
                new ActualizarNombreOrganizacionRequest(Nombre.Trim(), Version), Token(), HttpContext.RequestAborted);
            Nombre = Organizacion.Nombre;
            Version = Organizacion.Version;
            MostrarFormularioNombre = true;
            Confirmacion = "Nombre del operador actualizado.";
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await ManejarApiExceptionAsync(exception);
            if (redirect is not null) return redirect;
            MostrarFormularioNombre = exception.StatusCode != HttpStatusCode.Forbidden;
        }
        catch (InvalidOperationException) { return await CerrarSesionExpiradaAsync(); }
        catch (HttpRequestException)
        {
            Error = "No pudimos comunicarnos con la API de organizaciones.";
            MostrarFormularioNombre = true;
        }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        { Error = "La API tardó demasiado en responder. Intentá nuevamente."; }

        if (Organizacion is null)
        {
            var loaded = await CargarAsync(conservarEdicion: true);
            if (loaded is not null) return loaded;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSolicitarBajaAsync()
    {
        // The cancellation form is independent from the name editor; omitted edit fields must
        // not leave stale validation errors that block a later name update on this page.
        ModelState.Remove(nameof(Nombre));
        ModelState.Remove(nameof(Version));
        if (string.IsNullOrWhiteSpace(MotivoBaja) || MotivoBaja.Trim().Length > 2000 || !ConfirmarBaja)
        {
            Error = "Indicá un motivo de hasta 2000 caracteres y confirmá que entendés el efecto.";
            await CargarAsync();
            return Page();
        }
        try
        {
            await apiClient.SolicitarBajaAsync(new SolicitarBajaRequest(MotivoBaja.Trim()), Token(), HttpContext.RequestAborted);
            Confirmacion = "La solicitud quedó pendiente de resolución por plataforma. El operador continúa activo.";
            await CargarAsync();
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await ManejarApiExceptionAsync(exception);
            if (redirect is not null) return redirect;
            await CargarAsync();
        }
        catch (InvalidOperationException) { return await CerrarSesionExpiradaAsync(); }
        catch (HttpRequestException) { Error = "No pudimos enviar la solicitud porque la API no está disponible."; }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        { Error = "La API tardó demasiado en responder. Intentá nuevamente."; }
        MotivoBaja = "";
        ModelState.Remove(nameof(MotivoBaja));
        return Page();
    }

    private async Task<IActionResult?> CargarAsync(bool conservarEdicion = false)
    {
        var nombreEditado = Nombre;
        var versionEditada = Version;
        var errorAnterior = Error;
        try
        {
            Organizacion = await apiClient.ObtenerAsync(Token(), HttpContext.RequestAborted);
            MostrarFormularioNombre = Organizacion.EsPropietario;
            Nombre = Organizacion.Nombre;
            Version = Organizacion.Version;
            Solicitudes = await apiClient.ListarSolicitudesBajaAsync(Token(), HttpContext.RequestAborted);
        }
        catch (NexoRutaApiException exception)
        {
            return await ManejarApiExceptionAsync(exception);
        }
        catch (InvalidOperationException) { return await CerrarSesionExpiradaAsync(); }
        catch (HttpRequestException) { Error = "No pudimos consultar el operador porque la API no está disponible."; }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        { Error = "La API tardó demasiado en responder. Intentá nuevamente."; }

        if (conservarEdicion && errorAnterior is not null)
            Error = errorAnterior;

        if (conservarEdicion)
        {
            Nombre = nombreEditado;
            Version = versionEditada;
        }

        return null;
    }

    private async Task<IActionResult?> ManejarApiExceptionAsync(NexoRutaApiException exception)
    {
        if (exception.StatusCode == HttpStatusCode.Unauthorized) return await CerrarSesionExpiradaAsync();
        Error = exception.StatusCode switch
        {
            HttpStatusCode.Forbidden => exception.Message.Contains("dueño registrado", StringComparison.OrdinalIgnoreCase)
                ? exception.Message : "No tenés autorización para editar los datos del operador.",
            HttpStatusCode.BadRequest => exception.Message,
            HttpStatusCode.Conflict => "El operador cambió desde la última consulta o ya tiene una solicitud pendiente. Recargá esta página antes de volver a intentar.",
            HttpStatusCode.NotFound => exception.Message,
            _ => "No pudimos completar la operación porque la API no está disponible. Intentá más tarde."
        };
        return null;
    }

    private async Task<IActionResult> CerrarSesionExpiradaAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/ingresar?expirada=true");
    }

    private string Token() => SesionUsuario.ObtenerToken(User);
}
