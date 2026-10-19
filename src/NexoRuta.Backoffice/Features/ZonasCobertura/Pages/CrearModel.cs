using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.ZonasCobertura.Clients;
using NexoRuta.ApiClient.Features.ZonasCobertura.Contracts;
using NexoRuta.Backoffice.Core.Security;

namespace NexoRuta.Backoffice.Features.ZonasCobertura.Pages;

public sealed class CrearModel(ZonasCoberturaApiClient api, IConfiguration configuration) : PageModel
{
    public IReadOnlyList<DepartamentoReferenciaResponse> Departamentos { get; private set; } = [];
    public bool MapboxConfigured { get; private set; }
    public string? MapboxPublicToken { get; private set; }
    public string MapboxStyleUrl { get; private set; } = ZonaCoberturaUi.MapboxDefaultStyle;
    public string? Error { get; private set; }
    public bool CatalogoConsultado { get; private set; }
    public bool CatalogoVacio => CatalogoConsultado && Departamentos.Count == 0;

    [BindProperty, Required, StringLength(120)] public string Nombre { get; set; } = "";
    [BindProperty, Required] public string Fuente { get; set; } = ZonaCoberturaUi.FuenteDibujo;
    [BindProperty] public string? CodigoDepartamento { get; set; }
    [BindProperty] public string? GeometriaJson { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        ConfigureMap();
        var loaded = await LoadDepartmentsAsync();
        return loaded ?? Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ConfigureMap();
        if (!ModelState.IsValid)
            Error = "Ingresá un nombre de hasta 120 caracteres y elegí una fuente de geometría.";

        var sourceIsDrawing = Fuente == ZonaCoberturaUi.FuenteDibujo;
        var sourceIsDepartment = Fuente == ZonaCoberturaUi.FuenteDepartamento;
        if (!sourceIsDrawing && !sourceIsDepartment)
            Error ??= "Elegí una de las fuentes de geometría disponibles.";

        var hasDepartment = !string.IsNullOrWhiteSpace(CodigoDepartamento);
        var hasGeometry = !string.IsNullOrWhiteSpace(GeometriaJson);
        if (sourceIsDrawing && (!hasGeometry || hasDepartment) || sourceIsDepartment && (!hasDepartment || hasGeometry))
            Error ??= "Elegí exactamente una fuente: un polígono dibujado o un departamento del catálogo.";
        if (sourceIsDrawing && !MapboxConfigured)
            Error ??= "El mapa no está configurado. Configurá un token público de Mapbox antes de guardar una zona dibujada.";

        var loadResult = await LoadDepartmentsAsync();
        if (loadResult is not null) return loadResult;
        if (sourceIsDepartment && hasDepartment && !Departamentos.Any(x => string.Equals(x.Codigo, CodigoDepartamento, StringComparison.Ordinal)))
            Error ??= "El departamento seleccionado no está disponible en el catálogo local.";

        GeoJsonGeometryDto? geometry = null;
        if (sourceIsDrawing && hasGeometry && !ZonaCoberturaUi.TryParseGeometry(GeometriaJson, out geometry, out var geometryError))
            Error ??= geometryError;

        if (Error is not null || !ModelState.IsValid) return Page();

        try
        {
            var request = sourceIsDrawing
                ? new CrearZonaCoberturaRequest(Nombre.Trim(), geometry, null)
                : new CrearZonaCoberturaRequest(Nombre.Trim(), null, CodigoDepartamento);
            var created = await api.CrearAsync(request, SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            return RedirectToPage("/ZonasCobertura/Detalle", new { zonaId = created.Id });
        }
        catch (NexoRutaApiException exception)
        {
            var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
            if (expired is not null) return expired;
            Error = ZonaCoberturaUi.ErrorMessage(exception);
        }
        catch (HttpRequestException) { Error = "La API de zonas no está disponible. No se confirmó el guardado."; }

        return Page();
    }

    public async Task<IActionResult> OnGetTerritorioAsync()
    {
        try
        {
            var territory = await api.ObtenerTerritorioUruguayAsync(
                SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            return new JsonResult(territory);
        }
        catch (NexoRutaApiException exception)
        {
            var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
            if (expired is not null) return expired;
            return StatusCode((int)exception.StatusCode, new { message = ZonaCoberturaUi.ErrorMessage(exception) });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "No se pudo consultar la referencia territorial de Uruguay." });
        }
    }

    public async Task<IActionResult> OnGetDepartamentoAsync(string codigo)
    {
        try
        {
            var department = await api.ObtenerDepartamentoAsync(
                codigo, SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            var etag = $"\"{department.VersionFuente}-{department.Codigo}\"";
            Response.Headers["Cache-Control"] = "private, max-age=3600";
            Response.Headers["ETag"] = etag;
            if (Request.Headers["If-None-Match"].Any(value => string.Equals(value, etag, StringComparison.Ordinal)))
                return StatusCode(StatusCodes.Status304NotModified);
            return new JsonResult(department);
        }
        catch (NexoRutaApiException exception)
        {
            var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
            if (expired is not null) return expired;
            return StatusCode((int)exception.StatusCode, new { message = ZonaCoberturaUi.ErrorMessage(exception) });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "No se pudo consultar la geometría departamental." });
        }
    }

    private void ConfigureMap()
    {
        MapboxPublicToken = configuration[ZonaCoberturaUi.MapboxTokenKey];
        MapboxConfigured = MapboxPublicToken?.StartsWith("pk.", StringComparison.Ordinal) == true;
        if (!MapboxConfigured) MapboxPublicToken = null;
        MapboxStyleUrl = string.IsNullOrWhiteSpace(configuration[ZonaCoberturaUi.MapboxStyleKey])
            ? ZonaCoberturaUi.MapboxDefaultStyle : configuration[ZonaCoberturaUi.MapboxStyleKey]!;
    }

    private async Task<IActionResult?> LoadDepartmentsAsync()
    {
        try
        {
            Departamentos = await api.ListarDepartamentosAsync(SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            CatalogoConsultado = true;
        }
        catch (NexoRutaApiException exception)
        {
            var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
            if (expired is not null) return expired;
            Error ??= ZonaCoberturaUi.ErrorMessage(exception);
        }
        catch (HttpRequestException) { Error ??= "No se pudo consultar el catálogo local de departamentos."; }
        return null;
    }
}
