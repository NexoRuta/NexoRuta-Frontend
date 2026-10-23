using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.IdentidadVisual;
using NexoRuta.ApiClient.Features.IdentidadVisual.Clients;
using NexoRuta.ApiClient.Features.IdentidadVisual.Contracts.Requests;
using NexoRuta.ApiClient.Features.IdentidadVisual.Contracts.Responses;

namespace NexoRuta.Backoffice.Features.IdentidadVisual.Pages;

[Authorize(Policy = "configuracion.gestionar")]
public sealed class IdentidadVisualModel(IdentidadVisualApiClient client) : PageModel
{
    public IdentidadVisualAdministracionResponse? Datos { get; private set; }
    public IReadOnlyList<IdentidadVisualVersionResponse> Historial { get; private set; } = [];
    public int? CursorAnterior { get; private set; }
    public int? AntesDeVersion { get; private set; }
    public string? Error { get; private set; }
    public string? Confirmacion { get; private set; }

    [BindProperty] public int VersionEsperada { get; set; }
    [BindProperty] public string? NombreComercial { get; set; }
    [BindProperty] public string? CorreoPublico { get; set; }
    [BindProperty] public string? TelefonoPublico { get; set; }
    [BindProperty] public bool UsarColorPrimario { get; set; }
    [BindProperty] public string ColorPrimario { get; set; } = "#000000";
    [BindProperty] public bool UsarColorSecundario { get; set; }
    [BindProperty] public string ColorSecundario { get; set; } = "#000000";
    [BindProperty] public IFormFile? Logo { get; set; }
    [BindProperty] public bool QuitarLogo { get; set; }

    public string NombreVistaPrevia => string.IsNullOrWhiteSpace(NombreComercial)
        ? Datos?.NombreMostrado ?? "Nombre del operador" : NombreComercial.Trim();
    public string? LogoDataUri => Datos?.LogoContenido is { Length: > 0 } bytes
        && Datos.LogoTipoContenido is "image/png" or "image/jpeg"
            ? $"data:{Datos.LogoTipoContenido};base64,{Convert.ToBase64String(bytes)}" : null;
    public string AcentoPrimario => UsarColorPrimario
        ? ColorMarca.AcentoSobreBlanco(ColorPrimario) ?? "var(--bs-primary)" : "var(--bs-primary)";
    public string AcentoSecundario => UsarColorSecundario
        ? ColorMarca.AcentoSobreBlanco(ColorSecundario) ?? "var(--bs-secondary-color)" : "var(--bs-border-color)";
    public bool AvisarContrastePrimario => UsarColorPrimario && ColorMarca.AcentoSobreBlanco(ColorPrimario) is null;
    public bool AvisarContrasteSecundario => UsarColorSecundario && ColorMarca.AcentoSobreBlanco(ColorSecundario) is null;

    public async Task<IActionResult> OnGetAsync(int? antesDeVersion)
    {
        AntesDeVersion = antesDeVersion;
        var redirect = await CargarAsync(cargarFormulario: true, antesDeVersion);
        return redirect ?? Page();
    }

    public async Task<IActionResult> OnPostGuardarAsync()
    {
        if (VersionEsperada < 0)
        {
            Error = "La versión enviada no es válida. Recargá la configuración.";
            return await VolverConDatosAsync();
        }
        if (Logo is { Length: > 1_048_576 })
        {
            Error = "El logo no puede superar 1 MiB.";
            return await VolverConDatosAsync();
        }
        if (QuitarLogo && Logo is { Length: > 0 })
        {
            Error = "Elegí cargar un logo nuevo o quitar el actual, no ambas acciones.";
            return await VolverConDatosAsync();
        }

        byte[]? logo = null;
        if (Logo is { Length: > 0 } file)
        {
            await using var stream = new MemoryStream((int)file.Length);
            await file.CopyToAsync(stream, HttpContext.RequestAborted);
            logo = stream.ToArray();
        }
        try
        {
            Datos = await client.GuardarAsync(new ActualizarIdentidadVisualRequest(
                VersionEsperada, NombreComercial, CorreoPublico, TelefonoPublico,
                UsarColorPrimario ? ColorPrimario : null, UsarColorSecundario ? ColorSecundario : null,
                QuitarLogo, logo), Token(), HttpContext.RequestAborted);
            Confirmacion = "La identidad visual quedó guardada.";
            CargarFormularioDesdeDatos();
            ModelState.Clear();
            await CargarHistorialAsync();
        }
        catch (NexoRutaApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        { return await CerrarSesionAsync(); }
        catch (NexoRutaApiException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        { return Forbid(); }
        catch (NexoRutaApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            Error = exception.Message;
            var redirect = await CargarAsync(cargarFormulario: false);
            if (redirect is not null) return redirect;
        }
        catch (NexoRutaApiException exception)
        {
            Error = exception.Message;
            var redirect = await CargarAsync(cargarFormulario: false);
            if (redirect is not null) return redirect;
        }
        catch (InvalidOperationException) { return await CerrarSesionAsync(); }
        catch (HttpRequestException) { Error = "No pudimos comunicarnos con la API. Tus cambios siguen en el formulario."; }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        { Error = "La API tardó demasiado en responder. Tus cambios siguen en el formulario."; }

        return Page();
    }

    private async Task<IActionResult> VolverConDatosAsync()
    {
        var redirect = await CargarAsync(cargarFormulario: false);
        return redirect ?? Page();
    }

    private async Task<IActionResult?> CargarAsync(bool cargarFormulario, int? antesDeVersion = null)
    {
        try
        {
            Datos = await client.ObtenerAsync(Token(), HttpContext.RequestAborted);
            await CargarHistorialAsync(antesDeVersion);
            if (cargarFormulario) CargarFormularioDesdeDatos();
            return null;
        }
        catch (NexoRutaApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        { return await CerrarSesionAsync(); }
        catch (NexoRutaApiException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        { return Forbid(); }
        catch (NexoRutaApiException exception) { Error ??= exception.Message; return null; }
        catch (InvalidOperationException) { return await CerrarSesionAsync(); }
        catch (HttpRequestException) { Error ??= "No pudimos cargar la identidad visual. Intentá nuevamente."; return null; }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        { Error ??= "La consulta tardó demasiado. Intentá nuevamente."; return null; }
    }

    private async Task CargarHistorialAsync(int? antesDeVersion = null)
    {
        var page = await client.HistorialAsync(Token(), antesDeVersion, HttpContext.RequestAborted);
        Historial = page.Versiones;
        CursorAnterior = page.CursorAnterior;
    }

    private void CargarFormularioDesdeDatos()
    {
        VersionEsperada = Datos?.Version ?? 0;
        NombreComercial = Datos?.NombreComercial;
        CorreoPublico = Datos?.CorreoPublico;
        TelefonoPublico = Datos?.TelefonoPublico;
        UsarColorPrimario = Datos?.ColorPrimario is not null;
        ColorPrimario = Datos?.ColorPrimario ?? "#000000";
        UsarColorSecundario = Datos?.ColorSecundario is not null;
        ColorSecundario = Datos?.ColorSecundario ?? "#000000";
        QuitarLogo = false;
    }

    private async Task<IActionResult> CerrarSesionAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/ingresar?expirada=true");
    }

    private string Token() => SesionUsuario.ObtenerToken(User);
}
