using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Tarifas.Clients;
using NexoRuta.ApiClient.Features.Tarifas.Contracts;
using NexoRuta.ApiClient.Features.ZonasCobertura.Clients;
using NexoRuta.Backoffice.Features.Tarifas;

namespace NexoRuta.Backoffice.Features.Tarifas.Pages;

public sealed class BorradorModel(TarifasApiClient tarifasApi, ZonasCoberturaApiClient zonasApi) : TarifasPageModel
{
    [BindProperty] public string VersionConcurrencia { get; set; } = "";
    [BindProperty] public List<TarifaForm> Tarifas { get; set; } = [];
    public Guid VersionTarifarioId { get; private set; }
    public IReadOnlyList<ZonaTarifarioOption> Zonas { get; private set; } = [];
    public bool CatalogoCompleto { get; private set; }
    public bool LocalChangesPending { get; private set; }
    [TempData] public string? Notice { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            var version = await tarifasApi.ObtenerBorradorAsync(SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            Load(version);
            var catalogRedirect = await LoadZonesAsync();
            if (catalogRedirect is not null) return catalogRedirect;
            return Page();
        }
        catch (NexoRutaApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Notice = "Todavía no hay un borrador. Creá el inicial o una copia desde el estado del tarifario.";
            return RedirectToPage("/Tarifas/Index");
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await HandleApiErrorAsync(exception);
            if (redirect is not null) return redirect;
        }
        catch (HttpRequestException) { HandleTransportError(); }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { HandleTransportError(); }
        return Page();
    }

    public Task<IActionResult> OnPostAgregarTarifaAsync() => ReturnAfterLocalEditAsync(() =>
    {
        Tarifas ??= [];
        if (Tarifas.Count >= TarifasUi.MaxDraftRates)
        {
            Error = "El borrador alcanzó la cantidad máxima de tarifas.";
            return;
        }
        Tarifas.Add(new TarifaForm());
    });

    public Task<IActionResult> OnPostQuitarTarifaAsync(int indice) => ReturnAfterLocalEditAsync(() =>
    {
        if (Tarifas is not null && indice >= 0 && indice < Tarifas.Count)
            Tarifas.RemoveAt(indice);
    });

    public Task<IActionResult> OnPostAgregarAjusteAsync(int indiceTarifa) => ReturnAfterLocalEditAsync(() =>
    {
        if (Tarifas is null || indiceTarifa < 0 || indiceTarifa >= Tarifas.Count) return;
        var adjustments = Tarifas[indiceTarifa].Ajustes ??= [];
        if (adjustments.Count >= TarifasUi.MaxAdjustmentsPerRate)
        {
            Error = "Esta tarifa alcanzó la cantidad máxima de ajustes.";
            return;
        }
        adjustments.Add(new AjusteForm());
    });

    public Task<IActionResult> OnPostQuitarAjusteAsync(int indiceTarifa, int indiceAjuste)
        => ReturnAfterLocalEditAsync(() =>
        {
            if (Tarifas is null || indiceTarifa < 0 || indiceTarifa >= Tarifas.Count) return;
            var adjustments = Tarifas[indiceTarifa].Ajustes;
            if (adjustments is not null && indiceAjuste >= 0 && indiceAjuste < adjustments.Count)
                adjustments.RemoveAt(indiceAjuste);
        });

    public async Task<IActionResult> OnPostGuardarAsync()
    {
        LocalChangesPending = true;
        var catalogReloadRedirect = await LoadZonesAsync();
        if (catalogReloadRedirect is not null) return catalogReloadRedirect;
        if (!CatalogoCompleto)
        {
            Error ??= "No se pudo consultar el catálogo completo de zonas. No se guardó el borrador.";
            return Page();
        }

        if (!long.TryParse(VersionConcurrencia, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion))
            ModelState.AddModelError(nameof(VersionConcurrencia), "Recargá el borrador para obtener una versión válida.");

        if (!TryBuildRates(out var rates))
            Error ??= "Revisá los campos señalados. El borrador conserva tus entradas y todavía no se guardó.";

        if (!ModelState.IsValid) return Page();

        try
        {
            await tarifasApi.ReemplazarTarifasBorradorAsync(
                new ReemplazarTarifasBorradorRequest(expectedVersion, rates),
                SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            Notice = "El borrador se guardó. Todavía no está publicado.";
            return RedirectToPage("/Tarifas/Borrador");
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await HandleApiErrorAsync(exception);
            if (redirect is not null) return redirect;
        }
        catch (HttpRequestException) { HandleTransportError(); }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { HandleTransportError(); }

        var catalogRedirect = await LoadZonesAsync();
        if (catalogRedirect is not null) return catalogRedirect;
        return Page();
    }

    private async Task<IActionResult> ReturnAfterLocalEditAsync(Action edit)
    {
        LocalChangesPending = true;
        Tarifas ??= [];
        edit();
        ModelState.Clear();
        var catalogRedirect = await LoadZonesAsync();
        if (catalogRedirect is not null) return catalogRedirect;
        return Page();
    }

    private async Task<IActionResult?> LoadZonesAsync()
    {
        try
        {
            Zonas = await TarifasPageData.CargarTodasLasZonasAsync(zonasApi,
                SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            CatalogoCompleto = true;
            return null;
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await HandleApiErrorAsync(exception);
            if (redirect is not null) return redirect;
            Error ??= "No se pudo consultar el catálogo completo de zonas. No se guardará ningún reemplazo parcial.";
            CatalogoCompleto = false;
            return null;
        }
        catch (HttpRequestException)
        {
            HandleTransportError();
            CatalogoCompleto = false;
            return null;
        }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            HandleTransportError();
            CatalogoCompleto = false;
            return null;
        }
    }

    private void Load(VersionTarifarioResponse draft)
    {
        VersionTarifarioId = draft.VersionTarifarioId;
        VersionConcurrencia = draft.VersionConcurrencia.ToString(CultureInfo.InvariantCulture);
        Tarifas = draft.Tarifas.Select(rate => new TarifaForm
        {
            ZonaId = rate.ZonaId.ToString("D"),
            Modalidad = rate.Modalidad.ToString(),
            ImporteBaseUyu = TarifasUi.FormatDecimal(rate.ImporteBaseUyu, 2),
            PrecioPorKilogramoUyu = TarifasUi.FormatDecimal(rate.PrecioPorKilogramoUyu, 6),
            PrecioPorMetroCubicoUyu = TarifasUi.FormatDecimal(rate.PrecioPorMetroCubicoUyu, 6),
            Ajustes = rate.Ajustes.Select(adjustment => new AjusteForm
            {
                Nombre = adjustment.Nombre,
                Tipo = adjustment.Tipo.ToString(),
                Valor = adjustment.ImporteFijoUyu is { } amount
                    ? TarifasUi.FormatDecimal(amount, 2)
                    : adjustment.Porcentaje is { } percentage ? TarifasUi.FormatDecimal(percentage, 2) : ""
            }).ToList()
        }).ToList();
    }

    private bool TryBuildRates(out IReadOnlyList<TarifaZonaModalidadRequest> rates)
    {
        var result = new List<TarifaZonaModalidadRequest>();
        var combinations = new HashSet<(Guid Zone, ModalidadTarifaria Modality)>();
        var input = Tarifas ?? [];
        if (input.Count > TarifasUi.MaxDraftRates)
            ModelState.AddModelError(nameof(Tarifas), "El borrador supera la cantidad máxima de tarifas.");

        for (var i = 0; i < input.Count; i++)
        {
            var rate = input[i];
            var zoneKey = $"Tarifas[{i}].ZonaId";
            if (!Guid.TryParse(rate.ZonaId, out var zoneId) || !Zonas.Any(zone => zone.Id == zoneId))
            {
                ModelState.AddModelError(zoneKey, "Seleccioná una zona disponible de este operador.");
                continue;
            }
            if (!Enum.TryParse<ModalidadTarifaria>(rate.Modalidad, ignoreCase: false, out var modality) ||
                !Enum.IsDefined(modality))
            {
                ModelState.AddModelError($"Tarifas[{i}].Modalidad", "Seleccioná Estándar o Urgente.");
                continue;
            }
            if (!combinations.Add((zoneId, modality)))
                ModelState.AddModelError(zoneKey, "No puede haber más de una tarifa para la misma zona y modalidad.");

            var basePrice = ParseRateDecimal(rate.ImporteBaseUyu, $"Tarifas[{i}].ImporteBaseUyu", 2, "Precio base");
            var perKilogram = ParseRateDecimal(rate.PrecioPorKilogramoUyu, $"Tarifas[{i}].PrecioPorKilogramoUyu", 6, "Precio por kilogramo");
            var perCubicMeter = ParseRateDecimal(rate.PrecioPorMetroCubicoUyu, $"Tarifas[{i}].PrecioPorMetroCubicoUyu", 6, "Precio por metro cúbico");
            var adjustments = BuildAdjustments(rate.Ajustes, i);
            if (basePrice is null || perKilogram is null || perCubicMeter is null || adjustments is null) continue;
            result.Add(new TarifaZonaModalidadRequest(zoneId, modality, basePrice.Value,
                perKilogram.Value, perCubicMeter.Value, adjustments));
        }

        rates = result;
        return ModelState.IsValid && result.Count == input.Count;
    }

    private decimal? ParseRateDecimal(string? text, string key, int precision, string label)
    {
        if (TarifasUi.TryParseNonNegativeDecimal(text, precision, out var value)) return value;
        ModelState.AddModelError(key, $"Ingresá {label.ToLowerInvariant()} con hasta {precision} decimales, usando el separador decimal de tu configuración regional.");
        return null;
    }

    private IReadOnlyList<AjusteTarifarioRequest>? BuildAdjustments(IReadOnlyList<AjusteForm>? adjustments, int rateIndex)
    {
        var input = adjustments ?? [];
        if (input.Count > TarifasUi.MaxAdjustmentsPerRate)
        {
            ModelState.AddModelError($"Tarifas[{rateIndex}].Ajustes", "Se superó la cantidad máxima de ajustes por tarifa.");
            return null;
        }

        var result = new List<AjusteTarifarioRequest>();
        for (var i = 0; i < input.Count; i++)
        {
            var item = input[i];
            var prefix = $"Tarifas[{rateIndex}].Ajustes[{i}]";
            var name = item.Nombre?.Trim() ?? "";
            if (name.Length is < 1 or > 120)
                ModelState.AddModelError($"{prefix}.Nombre", "Ingresá un nombre de 1 a 120 caracteres.");
            if (!Enum.TryParse<TipoAjusteTarifario>(item.Tipo, ignoreCase: false, out var type) || !Enum.IsDefined(type))
            {
                ModelState.AddModelError($"{prefix}.Tipo", "Seleccioná un tipo de ajuste válido.");
                continue;
            }

            var isPercent = type is TipoAjusteTarifario.RecargoPorcentual or TipoAjusteTarifario.BonificacionPorcentual;
            if (!TarifasUi.TryParseNonNegativeDecimal(item.Valor, 2, out var value) || (isPercent && value > 100m))
            {
                ModelState.AddModelError($"{prefix}.Valor", isPercent
                    ? "Ingresá un porcentaje entre 0 y 100, con hasta dos decimales."
                    : "Ingresá un importe en UYU no negativo, con hasta dos decimales.");
                continue;
            }

            result.Add(new AjusteTarifarioRequest(name, type, isPercent ? null : value, isPercent ? value : null));
        }

        return ModelState.IsValid ? result : null;
    }

    public sealed class TarifaForm
    {
        public string ZonaId { get; set; } = "";
        public string Modalidad { get; set; } = "";
        public string ImporteBaseUyu { get; set; } = "";
        public string PrecioPorKilogramoUyu { get; set; } = "";
        public string PrecioPorMetroCubicoUyu { get; set; } = "";
        public List<AjusteForm> Ajustes { get; set; } = [];
    }

    public sealed class AjusteForm
    {
        public string Nombre { get; set; } = "";
        public string Tipo { get; set; } = "";
        public string Valor { get; set; } = "";
    }
}
