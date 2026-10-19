using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.ZonasCobertura.Clients;
using NexoRuta.ApiClient.Features.ZonasCobertura.Contracts;
using NexoRuta.Backoffice.Core.Security;

namespace NexoRuta.Backoffice.Features.ZonasCobertura.Pages;

public sealed class DetalleModel(ZonasCoberturaApiClient api, IConfiguration configuration) : PageModel
{
    public ZonaCoberturaDetalleResponse? Zona { get; private set; }
    public ConsultaCoberturaResponse? Cobertura { get; private set; }
    public bool MapboxConfigured { get; private set; }
    public bool GeometriaEditable { get; private set; }
    public string? MapboxPublicToken { get; private set; }
    public string MapboxStyleUrl { get; private set; } = ZonaCoberturaUi.MapboxDefaultStyle;
    public string? Error { get; private set; }
    public bool Conflicto { get; private set; }
    public string? Confirmacion => TempData["ZonasCobertura.Confirmacion"] as string;
    public string? ConflictoPrevio => TempData["ZonasCobertura.Conflicto"] as string;
    public bool AltaFranjaConError { get; private set; }
    public Guid? FranjaEditadaConError { get; private set; }

    [BindProperty, Required, StringLength(120)] public string Nombre { get; set; } = "";
    [BindProperty] public long? VersionEditada { get; set; }
    [BindProperty] public string? GeometriaJson { get; set; }
    [BindProperty] public short DiaSemanaIso { get; set; }
    [BindProperty] public string? HoraInicioHora { get; set; }
    [BindProperty] public string? HoraInicioMinuto { get; set; }
    [BindProperty] public string? HoraFinHora { get; set; }
    [BindProperty] public string? HoraFinMinuto { get; set; }
    [BindProperty] public string? EstadoFranja { get; set; }
    [BindProperty] public long? VersionZona { get; set; }
    [BindProperty] public string? Longitud { get; set; }
    [BindProperty] public string? Latitud { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid zonaId)
    {
        ConfigureMap();
        var result = await LoadZoneAsync(zonaId);
        if (result is not null) return result;
        if (Zona is not null)
        {
            Nombre = Zona.Nombre;
            VersionEditada = Zona.Version;
            GeometriaJson = ZonaCoberturaUi.GeometryJson(Zona.Geometria);
            VersionZona = Zona.Version;
        }
        if (ConflictoPrevio is not null) Error = ConflictoPrevio;
        return Page();
    }

    public async Task<IActionResult> OnGetTerritorioAsync(Guid zonaId)
    {
        try
        {
            var territory = await api.ObtenerTerritorioUruguayAsync(
                SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            var etag = $"\"{territory.VersionFuente}\"";
            Response.Headers["Cache-Control"] = "private, max-age=3600";
            Response.Headers["ETag"] = etag;
            if (Request.Headers.IfNoneMatch.Any(value => string.Equals(value, etag, StringComparison.Ordinal)))
                return StatusCode(StatusCodes.Status304NotModified);
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

    public async Task<IActionResult> OnPostEditarAsync(Guid zonaId)
    {
        ConfigureMap();
        var result = await LoadZoneAsync(zonaId);
        if (result is not null) return result;
        if (Zona is null) return Page();
        EnsureGeometryForView();
        var persisted = Zona;

        if (!ModelState.IsValid || VersionEditada is null or < 0)
        {
            Conflicto = VersionEditada is null or < 0;
            Error = "Ingresá un nombre válido y la versión leída del agregado.";
            return Page();
        }
        if (VersionEditada != persisted.Version)
        {
            Conflicto = true;
            Error = "La zona cambió mientras la editabas. Recargá para revisar la versión actual; no se aplicaron tus cambios.";
            return Page();
        }

        var geometry = persisted.Geometria;
        var originalJson = ZonaCoberturaUi.GeometryJson(persisted.Geometria);
        var geometryChanged = !string.Equals(GeometriaJson, originalJson, StringComparison.Ordinal);
        if (geometryChanged && MapboxConfigured && ZonaCoberturaUi.CanEditWithoutLosingParts(persisted.Geometria))
        {
            if (!ZonaCoberturaUi.TryParseGeometry(GeometriaJson, out geometry, out var geometryError))
            {
                Error = geometryError;
                return Page();
            }
        }
        geometry ??= persisted.Geometria;

        try
        {
            await api.ActualizarAsync(zonaId,
                new ActualizarZonaCoberturaRequest(Nombre.Trim(), geometry, VersionEditada.Value),
                SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            TempData["ZonasCobertura.Confirmacion"] = "La configuración de la zona se guardó.";
            return RedirectToPage("/ZonasCobertura/Detalle", new { zonaId });
        }
        catch (NexoRutaApiException exception)
        {
            var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
            if (expired is not null) return expired;
            Error = ZonaCoberturaUi.ErrorMessage(exception);
            Conflicto = exception.StatusCode == System.Net.HttpStatusCode.Conflict;
        }
        catch (HttpRequestException) { Error = "La API de zonas no está disponible. No se confirmó el guardado."; }
        return Page();
    }

    public async Task<IActionResult> OnPostEstadoAsync(Guid zonaId, string estado, long? version)
    {
        ConfigureMap();
        var result = await LoadZoneAsync(zonaId);
        if (result is not null) return result;
        if (Zona is null) return Page();
        EnsureGeometryForView();
        if (estado is not (ZonaCoberturaUi.EstadoActiva or ZonaCoberturaUi.EstadoInactiva) || version is null or < 0)
        {
            Conflicto = version is null or < 0;
            Error = "La operación de estado o la versión no son válidas.";
            return Page();
        }
        if (version != Zona.Version)
        {
            Conflicto = true;
            Error = "La zona cambió mientras la editabas. Recargá para revisar la versión actual; no se aplicaron tus cambios.";
            return Page();
        }
        VersionEditada ??= Zona.Version;
        if (estado == ZonaCoberturaUi.EstadoActiva && !Zona.Franjas.Any(x => x.Estado == ZonaCoberturaUi.EstadoActiva))
        {
            Error = "Agregá al menos una franja activa antes de activar la zona.";
            return Page();
        }

        try
        {
            await api.CambiarEstadoAsync(zonaId, new CambiarEstadoZonaRequest(estado, version.Value),
                SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            TempData["ZonasCobertura.Confirmacion"] = estado == ZonaCoberturaUi.EstadoActiva
                ? "La zona quedó activa." : "La zona quedó inactiva. Sus franjas se conservaron.";
            return RedirectToPage("/ZonasCobertura/Detalle", new { zonaId });
        }
        catch (NexoRutaApiException exception)
        {
            var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
            if (expired is not null) return expired;
            Error = ZonaCoberturaUi.ErrorMessage(exception);
            Conflicto = exception.StatusCode == System.Net.HttpStatusCode.Conflict;
        }
        catch (HttpRequestException) { Error = "La API de zonas no está disponible. No se confirmó el cambio de estado."; }
        return Page();
    }

    public async Task<IActionResult> OnPostCrearFranjaAsync(Guid zonaId)
    {
        ConfigureMap();
        var result = await LoadZoneAsync(zonaId);
        if (result is not null) return result;
        if (Zona is null) return Page();
        EnsureGeometryForView();
        AltaFranjaConError = true;
        if (VersionZona is null or < 0)
        {
            Conflicto = true;
            Error = "Recargá la zona para obtener una versión válida antes de guardar la franja.";
            return Page();
        }
        var version = VersionZona.Value;
        if (version != Zona.Version) return SetConflict();
        VersionEditada ??= Zona.Version;
        if (!ValidarFranja(null, null, out var startTime, out var endTime)) return Page();

        try
        {
            await api.CrearFranjaAsync(zonaId,
                new CrearFranjaEntregaRequest(DiaSemanaIso, startTime!, endTime!, version),
                SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            TempData["ZonasCobertura.Confirmacion"] = "La franja se agregó.";
            return RedirectToPage("/ZonasCobertura/Detalle", new { zonaId });
        }
        catch (NexoRutaApiException exception)
        {
            var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
            if (expired is not null) return expired;
            Error = ZonaCoberturaUi.ErrorMessage(exception);
            Conflicto = exception.StatusCode == System.Net.HttpStatusCode.Conflict;
        }
        catch (HttpRequestException) { Error = "La API de zonas no está disponible. No se confirmó el alta de la franja."; }
        return Page();
    }

    public async Task<IActionResult> OnPostEditarFranjaAsync(Guid zonaId, Guid franjaId)
    {
        ConfigureMap();
        var result = await LoadZoneAsync(zonaId);
        if (result is not null) return result;
        if (Zona is null) return Page();
        EnsureGeometryForView();
        FranjaEditadaConError = franjaId;
        if (VersionZona is null or < 0)
        {
            Conflicto = true;
            Error = "Recargá la zona para obtener una versión válida antes de guardar la franja.";
            return Page();
        }
        var version = VersionZona.Value;
        if (version != Zona.Version) return SetConflict();
        VersionEditada ??= Zona.Version;
        var franja = Zona.Franjas.FirstOrDefault(x => x.Id == franjaId);
        if (franja is null)
        {
            Response.StatusCode = 404;
            Error = "La franja no existe en esta zona.";
            return Page();
        }
        if (!ValidarFranja(franja.HoraInicio, franja.HoraFin, out var startTime, out var endTime)) return Page();
        if (EstadoFranja is not (ZonaCoberturaUi.EstadoActiva or ZonaCoberturaUi.EstadoInactiva))
        {
            Error = "Elegí un estado válido para la franja.";
            return Page();
        }

        try
        {
            await api.ActualizarFranjaAsync(zonaId, franjaId,
                new ActualizarFranjaEntregaRequest(DiaSemanaIso, startTime!, endTime!, EstadoFranja, version),
                SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            TempData["ZonasCobertura.Confirmacion"] = "La franja se actualizó.";
            return RedirectToPage("/ZonasCobertura/Detalle", new { zonaId });
        }
        catch (NexoRutaApiException exception)
        {
            var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
            if (expired is not null) return expired;
            Error = ZonaCoberturaUi.ErrorMessage(exception);
            Conflicto = exception.StatusCode == System.Net.HttpStatusCode.Conflict;
        }
        catch (HttpRequestException) { Error = "La API de zonas no está disponible. No se confirmó la edición de la franja."; }
        return Page();
    }

    public async Task<IActionResult> OnPostConsultarCoberturaAsync(Guid zonaId)
    {
        ConfigureMap();
        if (!double.TryParse(Longitud, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) ||
            !double.TryParse(Latitud, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
            !double.IsFinite(longitude) || !double.IsFinite(latitude) || longitude is < -180 or > 180 || latitude is < -90 or > 90)
            Error = "Seleccioná un punto válido en el mapa.";
        else
        {
            try
            {
                Cobertura = await api.ConsultarCoberturaAsync(new ConsultarCoberturaRequest(longitude, latitude),
                    SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            }
            catch (NexoRutaApiException exception)
            {
                var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
                if (expired is not null) return expired;
                Error = ZonaCoberturaUi.ErrorMessage(exception);
            }
            catch (HttpRequestException) { Error = "La API de cobertura no está disponible. Intentá nuevamente más tarde."; }
        }

        var result = await LoadZoneAsync(zonaId);
        if (result is not null) return result;
        if (Zona is not null)
        {
            EnsureGeometryForView();
            VersionEditada ??= Zona.Version;
            VersionZona ??= Zona.Version;
        }
        return Page();
    }

    private bool ValidarFranja(string? originalStart, string? originalEnd, out string? startTime, out string? endTime)
    {
        startTime = ZonaCoberturaUi.ComposeTime(HoraInicioHora, HoraInicioMinuto, originalStart);
        endTime = ZonaCoberturaUi.ComposeTime(HoraFinHora, HoraFinMinuto, originalEnd);
        if (DiaSemanaIso is < 1 or > 7 || startTime is null || endTime is null ||
            !Regex.IsMatch(startTime, ZonaCoberturaUi.TimePattern) || !Regex.IsMatch(endTime, ZonaCoberturaUi.TimePattern))
        {
            Error = "Elegí un día ISO y hora y minuto para el inicio y el fin.";
            return false;
        }
        if (TimeOnly.TryParse(startTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) &&
            TimeOnly.TryParse(endTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) && start == end)
        {
            Error = "Los horarios de inicio y fin deben ser distintos.";
            return false;
        }
        return true;
    }

    private async Task<IActionResult?> LoadZoneAsync(Guid zonaId)
    {
        try
        {
            Zona = await api.ObtenerAsync(zonaId, SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            GeometriaEditable = ZonaCoberturaUi.CanEditWithoutLosingParts(Zona.Geometria);
            MapboxPublicToken = configuration[ZonaCoberturaUi.MapboxTokenKey];
            MapboxConfigured = MapboxPublicToken?.StartsWith("pk.", StringComparison.Ordinal) == true;
            if (!MapboxConfigured) MapboxPublicToken = null;
            MapboxStyleUrl = string.IsNullOrWhiteSpace(configuration[ZonaCoberturaUi.MapboxStyleKey])
                ? ZonaCoberturaUi.MapboxDefaultStyle : configuration[ZonaCoberturaUi.MapboxStyleKey]!;
        }
        catch (NexoRutaApiException exception)
        {
            var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
            if (expired is not null) return expired;
            Response.StatusCode = exception.StatusCode == System.Net.HttpStatusCode.NotFound ? 404 : 200;
            Error = ZonaCoberturaUi.ErrorMessage(exception);
        }
        catch (HttpRequestException) { Error = "La API de zonas no está disponible. Intentá nuevamente más tarde."; }
        return null;
    }

    private IActionResult SetConflict()
    {
        Conflicto = true;
        Error = "La zona cambió mientras la editabas. Recargá para revisar la versión actual; no se aplicaron tus cambios.";
        return Page();
    }

    private void EnsureGeometryForView()
    {
        if (GeometriaJson is null && Zona is not null)
            GeometriaJson = ZonaCoberturaUi.GeometryJson(Zona.Geometria);
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
