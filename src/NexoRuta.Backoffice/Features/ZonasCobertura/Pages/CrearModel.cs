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
    public bool MapboxConfigured { get; private set; }
    public string? MapboxPublicToken { get; private set; }
    public string MapboxStyleUrl { get; private set; } = ZonaCoberturaUi.MapboxDefaultStyle;
    public string? Error { get; private set; }

    [BindProperty, Required, StringLength(120)] public string Nombre { get; set; } = "";
    [BindProperty, Required] public string Fuente { get; set; } = ZonaCoberturaUi.FuenteDibujo;
    [BindProperty] public string? CodigoDepartamento { get; set; }
    [BindProperty] public string? GeometriaJson { get; set; }

    public IActionResult OnGet()
    {
        ConfigureMap();
        return Page();
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
            Error ??= "Elegí exactamente una fuente: un polígono dibujado o un departamento oficial.";
        if (sourceIsDrawing && !MapboxConfigured)
            Error ??= "El mapa no está configurado. Configurá un token público de Mapbox antes de guardar una zona dibujada.";

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

    private void ConfigureMap()
    {
        MapboxPublicToken = configuration[ZonaCoberturaUi.MapboxTokenKey];
        MapboxConfigured = MapboxPublicToken?.StartsWith("pk.", StringComparison.Ordinal) == true;
        if (!MapboxConfigured) MapboxPublicToken = null;
        MapboxStyleUrl = string.IsNullOrWhiteSpace(configuration[ZonaCoberturaUi.MapboxStyleKey])
            ? ZonaCoberturaUi.MapboxDefaultStyle : configuration[ZonaCoberturaUi.MapboxStyleKey]!;
    }
}
