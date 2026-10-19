using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Features.Tarifas.Contracts;
using NexoRuta.ApiClient.Features.ZonasCobertura.Contracts;
using NexoRuta.Backoffice.Core.Security;
using NexoRuta.Backoffice.Features.ZonasCobertura;

namespace NexoRuta.Backoffice.Features.Tarifas;

internal static class TarifasUi
{
    public const string PermissionManage = ZonaCoberturaUi.PermisoGestionar;
    public const string HomeRoute = "/tarifas";
    public const string DraftRoute = "/tarifas/borrador";
    public const string HistoryRoute = "/tarifas/publicaciones";
    public const string PublishRoute = "/tarifas/publicar";
    public const string SimulationRoute = "/tarifas/simular";
    public const int ZonePageSize = 50;
    public const int HistoryPageSize = 100;
    public const int MaxDraftRates = 500;
    public const int MaxAdjustmentsPerRate = 100;
    public const int MaxParcels = 100;
    public const string ActiveZone = "Activa";
    public const string InactiveZone = "Inactiva";
    public const string StandardModality = "Estandar";
    public const string UrgentModality = "Urgente";

    public static string ErrorMessage(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.BadRequest => "La API rechazó los datos. Revisá los importes, ajustes y selecciones; no se guardó el borrador.",
        HttpStatusCode.Forbidden => "No tenés permiso vigente para gestionar las tarifas de este operador.",
        HttpStatusCode.NotFound => "No se encontró el borrador, publicación, zona o tarifa solicitada para este operador.",
        HttpStatusCode.Conflict => "El borrador cambió o no puede realizarse esta operación. Recargá para revisar el estado actual; no se sobrescribieron cambios.",
        _ => "No se pudo completar la operación. La API puede no estar disponible; intentá nuevamente más tarde."
    };

    public static bool TryParseNonNegativeDecimal(string? text, int? maxFractionDigits, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 48 ||
            !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.CurrentCulture, out value) || value < 0)
            return false;

        return maxFractionDigits is null || decimal.Round(value, maxFractionDigits.Value, MidpointRounding.ToZero) == value;
    }

    public static string FormatDecimal(decimal value, int maxFractionDigits)
        => value.ToString(maxFractionDigits == 0 ? "0" : $"0.{new string('#', maxFractionDigits)}",
            CultureInfo.CurrentCulture);

    public static string FormatExact(decimal value) => value.ToString("G29", CultureInfo.CurrentCulture);

    public static string FormatTotal(decimal value) => value.ToString("F2", CultureInfo.CurrentCulture);

    public static string AdjustmentLabel(TipoAjusteTarifario type)
        => AdjustmentPresentation(type).TypeLabel;

    public static TarifasAdjustmentPresentation AdjustmentPresentation(string? type)
        => Enum.TryParse<TipoAjusteTarifario>(type, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? AdjustmentPresentation(parsed)
            : new("Ajuste", "Valor", "Configurá el valor del ajuste.", string.Empty);

    public static TarifasAdjustmentPresentation AdjustmentPresentation(TipoAjusteTarifario type) => type switch
    {
        TipoAjusteTarifario.RecargoFijo => new("Sumar un importe fijo ($)", "Importe a sumar (UYU)",
            "Se aplica una vez por envío.", "Hasta 2 decimales."),
        TipoAjusteTarifario.RecargoPorcentual => new("Sumar un porcentaje (%)", "Porcentaje a sumar (%)",
            "Se calcula sobre el subtotal de base, peso y volumen.", "Hasta 2 decimales, entre 0 y 100."),
        TipoAjusteTarifario.BonificacionFija => new("Descontar un importe fijo ($)", "Importe a descontar (UYU)",
            "Se aplica una vez por envío.", "Hasta 2 decimales."),
        TipoAjusteTarifario.BonificacionPorcentual => new("Descontar un porcentaje (%)", "Porcentaje a descontar (%)",
            "Se calcula sobre el subtotal de base, peso y volumen.", "Hasta 2 decimales, entre 0 y 100."),
        _ => new("Ajuste", "Valor", "Configurá el valor del ajuste.", string.Empty)
    };

    public static string AdjustmentValue(AjusteTarifarioResponse adjustment)
    {
        if (adjustment.Tipo is TipoAjusteTarifario.RecargoPorcentual or TipoAjusteTarifario.BonificacionPorcentual)
            return adjustment.Porcentaje is { } percentage ? $"{FormatDecimal(percentage, 2)}%" : "Valor no disponible";
        return adjustment.ImporteFijoUyu is { } amount ? $"UYU {FormatDecimal(amount, 2)}" : "Valor no disponible";
    }
}

internal sealed record TarifasAdjustmentPresentation(
    string TypeLabel,
    string ValueLabel,
    string HelpText,
    string PrecisionHint);

public sealed record ZonaTarifarioOption(Guid Id, string Nombre, string Estado);

public abstract class TarifasPageModel : PageModel
{
    public string? Error { get; protected set; }
    public bool Conflict { get; protected set; }

    protected async Task<IActionResult?> HandleApiErrorAsync(NexoRutaApiException exception)
    {
        var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
        if (expired is not null) return expired;
        Conflict = exception.StatusCode == HttpStatusCode.Conflict;
        Error = TarifasUi.ErrorMessage(exception.StatusCode ?? HttpStatusCode.ServiceUnavailable);
        return null;
    }

    protected void HandleTransportError()
        => Error = "No se pudo comunicar con la API. Revisá tu conexión e intentá nuevamente más tarde.";
}

internal static class TarifasPageData
{
    public static async Task<IReadOnlyList<ZonaTarifarioOption>> CargarTodasLasZonasAsync(
        NexoRuta.ApiClient.Features.ZonasCobertura.Clients.ZonasCoberturaApiClient client,
        string token,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, ZonaTarifarioOption>();
        foreach (var state in new[] { TarifasUi.ActiveZone, TarifasUi.InactiveZone })
        {
            var pageNumber = 1;
            int total;
            do
            {
                var page = await client.ListarAsync(token, state, pageNumber, TarifasUi.ZonePageSize, cancellationToken);
                total = page.Total;
                foreach (var zone in page.Items)
                    result[zone.Id] = new ZonaTarifarioOption(zone.Id, zone.Nombre, zone.Estado);
                pageNumber++;
            }
            while ((long)(pageNumber - 1) * TarifasUi.ZonePageSize < total);
        }

        return result.Values.OrderBy(zone => zone.Nombre, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static async Task<IReadOnlyList<VersionTarifarioResumenResponse>> CargarTodasLasPublicacionesAsync(
        NexoRuta.ApiClient.Features.Tarifas.Clients.TarifasApiClient client,
        string token,
        CancellationToken cancellationToken)
    {
        var items = new List<VersionTarifarioResumenResponse>();
        var pageNumber = 1;
        int total;
        do
        {
            var page = await client.ListarPublicacionesAsync(token, pageNumber,
                TarifasUi.HistoryPageSize, cancellationToken);
            items.AddRange(page.Items);
            total = page.Total;
            pageNumber++;
        }
        while ((long)(pageNumber - 1) * TarifasUi.HistoryPageSize < total);

        return items;
    }
}
