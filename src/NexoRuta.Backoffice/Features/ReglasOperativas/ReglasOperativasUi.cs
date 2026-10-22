using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Features.ReglasOperativas.Contracts;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.Backoffice.Core.Security;

namespace NexoRuta.Backoffice.Features.ReglasOperativas;

internal static class ReglasOperativasUi
{
    public const string PermissionManage = "configuracion.gestionar";
    public const string HomeRoute = "/reglas-operativas";
    public const int MaxMotives = 100;
    public const int MaxAttempts = 100;
    public const int MaxDurationMinutes = 525_600;
    public const int MaxDurationDays = MaxDurationMinutes / 1_440;
    public const int DefaultHistoryPageSize = 20;
    public const string DraftCollectionMarker = "ColeccionesEnviadas";

    public static readonly IReadOnlyList<(TipoPruebaEntrega Value, string Label)> ProofOptions =
    [
        (TipoPruebaEntrega.FirmaEnPantalla, "Firma en pantalla"),
        (TipoPruebaEntrega.Fotografia, "Fotografía"),
        (TipoPruebaEntrega.NombreReceptor, "Nombre del receptor"),
        (TipoPruebaEntrega.DocumentoReceptor, "Documento del receptor")
    ];

    public static string ProofLabel(TipoPruebaEntrega value)
        => ProofOptions.FirstOrDefault(option => option.Value == value).Label ?? "Requisito de prueba";

    public static string PolicyLabel(PoliticaDevolucion? value) => value switch
    {
        PoliticaDevolucion.DecisionOperativa => "Un responsable decide cómo continuar",
        PoliticaDevolucion.IniciarAutomaticamente => "Iniciar la devolución automáticamente",
        _ => "Sin configurar"
    };

    public static string PublicationStatusLabel(bool vigente) => vigente ? "Vigente" : "Anterior";

    public static string MotiveAvailabilityLabel(bool available) => available ? "Disponible" : "No disponible";

    public static string DurationLabel(int? totalMinutes)
    {
        if (totalMinutes is null) return "Sin completar";
        var total = totalMinutes.Value;
        var days = total / 1_440;
        var hours = total % 1_440 / 60;
        var minutes = total % 60;
        var parts = new List<string>(3);
        if (days > 0) parts.Add($"{days} {(days == 1 ? "día" : "días")}");
        if (hours > 0) parts.Add($"{hours} {(hours == 1 ? "hora" : "horas")}");
        if (minutes > 0 || parts.Count == 0) parts.Add($"{minutes} {(minutes == 1 ? "minuto" : "minutos")}");
        return string.Join(", ", parts);
    }

    public static string ErrorMessage(NexoRutaApiException exception) => exception.StatusCode switch
    {
        HttpStatusCode.BadRequest => string.IsNullOrWhiteSpace(exception.Message)
            ? "La API rechazó los datos. Revisá los campos indicados."
            : exception.Message,
        HttpStatusCode.Conflict => "Las reglas cambiaron desde que las cargaste. Recargá para revisar el borrador; no se aplicaron tus cambios.",
        _ => "No se pudo completar la operación. Intentá nuevamente más tarde."
    };

    public static int[] Values(int count) => Enumerable.Range(0, count).ToArray();

    public static bool TryOptionalPositiveInt(string? text, int maximum, string field,
        ModelStateDictionary modelState, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (text.Length > 10 || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
            parsed is < 1 || parsed > maximum)
        {
            modelState.AddModelError(field, $"Ingresá un número entero entre 1 y {maximum}.");
            return false;
        }
        value = parsed;
        return true;
    }

    public static bool TryPolicy(string? text, string field, ModelStateDictionary modelState,
        out PoliticaDevolucion? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (Enum.TryParse<PoliticaDevolucion>(text, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed))
        {
            value = parsed;
            return true;
        }
        modelState.AddModelError(field, "Elegí una opción de devolución disponible.");
        return false;
    }

    public static bool TryDuration(DurationForm form, string field, ModelStateDictionary modelState,
        out int? minutes)
    {
        minutes = null;
        var allEmpty = string.IsNullOrWhiteSpace(form.Dias) && string.IsNullOrWhiteSpace(form.Horas) &&
            string.IsNullOrWhiteSpace(form.Minutos);
        if (allEmpty) return true;

        var valid = TryDays(form.Dias, field + ".Dias", modelState, out var days);
        valid &= TryPart(form.Horas, 23, field + ".Horas", modelState, "La hora debe estar entre 00 y 23.", out var hours);
        valid &= TryPart(form.Minutos, 59, field + ".Minutos", modelState, "Los minutos deben estar entre 00 y 59.", out var remainder);
        if (!valid) return false;

        var total = days * 1_440L + hours * 60L + remainder;
        if (total is < 1 or > MaxDurationMinutes)
        {
            modelState.AddModelError(field,
                total == 0 ? "La duración debe ser mayor que cero." : $"La duración no puede superar {MaxDurationDays} días.");
            return false;
        }
        minutes = (int)total;
        return true;
    }

    public static DurationForm Duration(int? totalMinutes)
    {
        if (totalMinutes is null) return new();
        var total = totalMinutes.Value;
        return new()
        {
            Dias = (total / 1_440).ToString(CultureInfo.InvariantCulture),
            Horas = ((total % 1_440) / 60).ToString("00", CultureInfo.InvariantCulture),
            Minutos = (total % 60).ToString("00", CultureInfo.InvariantCulture)
        };
    }

    private static bool TryDays(string? text, string field, ModelStateDictionary modelState, out int days)
    {
        days = 0;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (text.Length > 3 || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out days) ||
            days < 0 || days > MaxDurationDays)
        {
            modelState.AddModelError(field, $"Los días deben ser un entero entre 0 y {MaxDurationDays}.");
            return false;
        }
        return true;
    }

    private static bool TryPart(string? text, int maximum, string field, ModelStateDictionary modelState,
        string message, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (text.Length != 2 || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) ||
            value < 0 || value > maximum || text != value.ToString("00", CultureInfo.InvariantCulture))
        {
            modelState.AddModelError(field, message);
            return false;
        }
        return true;
    }
}

public sealed class DurationForm
{
    public string? Dias { get; set; }
    public string? Horas { get; set; }
    public string? Minutos { get; set; }
}

public sealed record DurationFieldsViewModel(string Prefix, string Label, DurationForm Value);

public sealed class MotivoForm
{
    public string? Codigo { get; set; }
    public string? Descripcion { get; set; }
    public bool Disponible { get; set; }
    public bool EsNuevo { get; set; }
}

public abstract class ReglasOperativasPageModel : PageModel
{
    public string? Error { get; protected set; }
    public bool Conflict { get; protected set; }
    [TempData] public string? Notice { get; set; }

    protected async Task<IActionResult?> HandleApiErrorAsync(NexoRutaApiException exception)
    {
        var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
        if (expired is not null) return expired;
        if (exception.StatusCode == HttpStatusCode.NotFound) return NotFound();
        if (exception.StatusCode == HttpStatusCode.Forbidden)
            return RedirectToPage("/AccesoDenegado");
        Conflict = exception.StatusCode == HttpStatusCode.Conflict;
        Error = ReglasOperativasUi.ErrorMessage(exception);
        return null;
    }

    protected void HandleTransportError()
        => Error = "No se pudo comunicar con la API. Revisá tu conexión e intentá nuevamente más tarde.";
}
