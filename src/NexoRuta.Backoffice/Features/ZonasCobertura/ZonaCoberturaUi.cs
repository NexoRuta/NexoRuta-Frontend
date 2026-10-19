using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Features.ZonasCobertura.Contracts;

namespace NexoRuta.Backoffice.Features.ZonasCobertura;

internal static class ZonaCoberturaUi
{
    public const string PermisoGestionar = "configuracion.gestionar";
    public const string EstadoActiva = "Activa";
    public const string EstadoInactiva = "Inactiva";
    public const string ResultadoCubierta = "Cubierta";
    public const string ResultadoSinCobertura = "SinCobertura";
    public const string FuenteDibujo = "Dibujo";
    public const string FuenteDepartamento = "Departamento";
    public const string FiltroActivas = "Activas";
    public const string FiltroInactivas = "Inactivas";
    public const string FiltroTodas = "Todas";
    public const string MapboxTokenKey = "Mapbox:PublicAccessToken";
    public const string MapboxStyleKey = "Mapbox:StyleUrl";
    public const string ListRoute = "/zonas";
    public const string MapboxDefaultStyle = "mapbox://styles/mapbox/streets-v12";
    public const string MapboxGlJsVersion = "3.33.0";
    public const string MapboxDrawVersion = "1.5.1";
    public const int MaxGeometryBytes = 1_048_576;
    public const string TimePattern = @"^(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,6})?$";
    private const string ConcurrencyConflictToken = "configuración de la zona cambió";
    private const string ActiveZoneConflictToken = "no se puede mantener activa";
    private const string ActiveSlotConflictToken = "requiere al menos una franja activa";
    private const string OverlappingSlotConflictToken = "no pueden superponerse";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string ErrorMessage(NexoRutaApiException exception) => exception.StatusCode switch
    {
        System.Net.HttpStatusCode.BadRequest => "La API rechazó los datos. Revisá el nombre, la geometría, el horario o la selección de departamento.",
        System.Net.HttpStatusCode.Forbidden => "No tenés permiso vigente para gestionar las zonas de este operador.",
        System.Net.HttpStatusCode.NotFound => "La zona, franja o referencia ya no existe para este operador.",
        System.Net.HttpStatusCode.Conflict => ConflictMessage(exception.Message),
        _ => "No se pudo completar la operación. Intentá nuevamente más tarde."
    };

    private static string ConflictMessage(string apiMessage)
    {
        if (apiMessage.Contains(ConcurrencyConflictToken, StringComparison.OrdinalIgnoreCase))
            return "La zona cambió mientras la editabas. Recargá para revisar la versión actual; no se aplicaron tus cambios.";
        if (apiMessage.Contains(ActiveZoneConflictToken, StringComparison.OrdinalIgnoreCase) ||
            apiMessage.Contains(ActiveSlotConflictToken, StringComparison.OrdinalIgnoreCase))
            return "Una zona activa debe conservar al menos una franja activa. Revisá las franjas antes de volver a guardar.";
        if (apiMessage.Contains(OverlappingSlotConflictToken, StringComparison.OrdinalIgnoreCase))
            return "Las franjas activas se superponen. Ajustá el día o el horario; no se aplicaron los cambios.";
        return "El cambio entra en conflicto con la configuración vigente. Recargá y revisá las reglas antes de volver a intentar; no se aplicaron tus cambios.";
    }

    public static bool TryParseGeometry(string? json, out GeoJsonGeometryDto? geometry, out string? error)
    {
        geometry = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxGeometryBytes)
        {
            error = "Dibujá una geometría válida de hasta 1 MiB.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var typeElement) ||
                !root.TryGetProperty("coordinates", out var coordinates) ||
                coordinates.ValueKind != JsonValueKind.Array)
            {
                error = "La geometría debe ser un objeto GeoJSON Polygon o MultiPolygon.";
                return false;
            }

            var type = typeElement.GetString();
            if (type is not ("Polygon" or "MultiPolygon"))
            {
                error = "La geometría debe ser Polygon o MultiPolygon.";
                return false;
            }

            geometry = new GeoJsonGeometryDto(type, coordinates.Clone());
            return true;
        }
        catch (JsonException)
        {
            error = "La geometría dibujada no tiene un formato GeoJSON válido.";
            return false;
        }
    }

    public static string GeometryJson(GeoJsonGeometryDto geometry)
        => JsonSerializer.Serialize(geometry, JsonOptions);

    public static bool CanEditWithoutLosingParts(GeoJsonGeometryDto geometry)
    {
        try
        {
            var coordinates = geometry.Coordinates;
            if (coordinates.ValueKind != JsonValueKind.Array) return false;
            if (geometry.Type == "Polygon")
                return coordinates.GetArrayLength() == 1 && coordinates[0].ValueKind == JsonValueKind.Array;
            if (geometry.Type == "MultiPolygon" && coordinates.GetArrayLength() == 1)
            {
                var polygon = coordinates[0];
                return polygon.ValueKind == JsonValueKind.Array && polygon.GetArrayLength() == 1 &&
                    polygon[0].ValueKind == JsonValueKind.Array;
            }
        }
        catch (InvalidOperationException) { }
        return false;
    }

    public static bool IsOvernight(FranjaEntregaResponse slot)
        => TimeOnly.TryParse(slot.HoraInicio, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) &&
           TimeOnly.TryParse(slot.HoraFin, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) && end < start;

    public static string? ComposeTime(string? hourText, string? minuteText, string? originalTime = null)
    {
        if (!TryParsePart(hourText, 23, out var hour) || !TryParsePart(minuteText, 59, out var minute)) return null;
        var hourValue = hour.ToString("00", CultureInfo.InvariantCulture);
        var minuteValue = minute.ToString("00", CultureInfo.InvariantCulture);
        if (originalTime is not null && Regex.IsMatch(originalTime, TimePattern) &&
            TimeOnly.TryParse(originalTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) &&
            HourPart(originalTime) == hourValue && MinutePart(originalTime) == minuteValue)
            return originalTime;

        return string.Concat(hourValue, ":", minuteValue, ":00.000000");
    }

    public static string? HourPart(string? time)
        => time is { Length: >= 5 } && time[2] == ':' ? time[..2] : null;

    public static string? MinutePart(string? time)
        => time is { Length: >= 5 } && time[2] == ':' ? time[3..5] : null;

    public static string TimeGuidance(string? startTime, string? endTime)
    {
        if (!TimeOnly.TryParse(startTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !TimeOnly.TryParse(endTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)) return string.Empty;
        if (start == end) return "Los horarios de inicio y fin deben ser distintos.";
        return end < start ? "Finaliza al día siguiente." : string.Empty;
    }

    private static bool TryParsePart(string? text, int maximum, out int value)
    {
        value = 0;
        return text is { Length: 2 } && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
            value <= maximum && text == value.ToString("00", CultureInfo.InvariantCulture);
    }

    public static string DayName(short day) => day switch
    {
        1 => "Lunes", 2 => "Martes", 3 => "Miércoles", 4 => "Jueves",
        5 => "Viernes", 6 => "Sábado", 7 => "Domingo", _ => "Día inválido"
    };
}
