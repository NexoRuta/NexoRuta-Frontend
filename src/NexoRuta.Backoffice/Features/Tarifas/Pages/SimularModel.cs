using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Tarifas.Clients;
using NexoRuta.ApiClient.Features.Tarifas.Contracts;
using NexoRuta.ApiClient.Features.ZonasCobertura.Clients;
using NexoRuta.Backoffice.Features.Tarifas;

namespace NexoRuta.Backoffice.Features.Tarifas.Pages;

public sealed class SimularModel(TarifasApiClient tarifasApi, ZonasCoberturaApiClient zonasApi) : TarifasPageModel
{
    [BindProperty] public string VersionTarifarioId { get; set; } = "";
    [BindProperty] public string ZonaId { get; set; } = "";
    [BindProperty] public string Modalidad { get; set; } = "";
    [BindProperty] public List<BultoForm> Bultos { get; set; } = [new()];
    public IReadOnlyList<VersionOption> Versiones { get; private set; } = [];
    public IReadOnlyList<ZonaTarifarioOption> Zonas { get; private set; } = [];
    public IReadOnlyList<ModalidadTarifaria> Modalidades { get; private set; } = [];
    public VersionTarifarioResponse? VersionSeleccionada { get; private set; }
    public SimulacionTarifaResponse? Resultado { get; private set; }
    public bool ZonaSeleccionadaInactiva { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid? versionTarifarioId)
    {
        if (versionTarifarioId is { } id) VersionTarifarioId = id.ToString("D");
        var redirect = await LoadOptionsAsync();
        if (redirect is not null) return redirect;
        if (versionTarifarioId is not null)
        {
            redirect = await LoadSelectedVersionAsync(clearZone: false);
            if (redirect is not null) return redirect;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostCargarVersionAsync()
    {
        ZonaId = "";
        Modalidad = "";
        ModelState.Clear();
        var redirect = await LoadOptionsAsync();
        if (redirect is not null) return redirect;
        redirect = await LoadSelectedVersionAsync(clearZone: true);
        return redirect ?? Page();
    }

    public async Task<IActionResult> OnPostCargarZonaAsync()
    {
        Modalidad = "";
        var redirect = await LoadOptionsAsync();
        if (redirect is not null) return redirect;
        redirect = await LoadSelectedVersionAsync(clearZone: false);
        if (redirect is not null) return redirect;
        if (VersionSeleccionada is not null && Guid.TryParse(ZonaId, out var zoneId))
            Modalidades = VersionSeleccionada.Tarifas.Where(rate => rate.ZonaId == zoneId)
                .Select(rate => rate.Modalidad).Distinct().OrderBy(modality => modality).ToArray();
        if (Modalidades.Count == 0) Error ??= "Esa versión no tiene tarifas configuradas para la zona elegida.";
        return Page();
    }

    public async Task<IActionResult> OnPostAgregarBultoAsync()
    {
        Bultos ??= [];
        if (Bultos.Count < TarifasUi.MaxParcels) Bultos.Add(new());
        else Error = "Se alcanzó la cantidad máxima de bultos para simular.";
        return await ReloadSelectionsAsync();
    }

    public async Task<IActionResult> OnPostQuitarBultoAsync(int indice)
    {
        Bultos ??= [];
        if (Bultos.Count > 1 && indice >= 0 && indice < Bultos.Count) Bultos.RemoveAt(indice);
        return await ReloadSelectionsAsync();
    }

    public async Task<IActionResult> OnPostSimularAsync()
    {
        var redirect = await LoadOptionsAsync();
        if (redirect is not null) return redirect;
        redirect = await LoadSelectedVersionAsync(clearZone: false);
        if (redirect is not null) return redirect;
        if (VersionSeleccionada is null) return Page();

        var zoneValid = Guid.TryParse(ZonaId, out var zoneId);
        var modalityValid = Enum.TryParse<ModalidadTarifaria>(Modalidad, ignoreCase: false, out var modality) && Enum.IsDefined(modality);
        if (!zoneValid || !modalityValid || !VersionSeleccionada.Tarifas.Any(rate => rate.ZonaId == zoneId && rate.Modalidad == modality))
            ModelState.AddModelError(nameof(ZonaId), "Seleccioná una zona y modalidad configuradas en la versión elegida.");

        var parcels = BuildParcels();
        if (!ModelState.IsValid) return Page();

        try
        {
            Resultado = await tarifasApi.SimularAsync(new SimularTarifaRequest(
                Guid.Parse(VersionTarifarioId), zoneId, modality, parcels),
                SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            ZonaSeleccionadaInactiva = Zonas.FirstOrDefault(zone => zone.Id == zoneId)?.Estado == TarifasUi.InactiveZone;
        }
        catch (NexoRutaApiException exception)
        {
            var apiRedirect = await HandleApiErrorAsync(exception);
            if (apiRedirect is not null) return apiRedirect;
        }
        catch (HttpRequestException) { HandleTransportError(); }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { HandleTransportError(); }
        return Page();
    }

    public string VersionLabel(VersionOption version) => version.IsDraft
        ? $"Borrador guardado (versión de concurrencia {version.Version.VersionConcurrencia})"
        : $"Publicación {version.Version.NumeroVersionPublicada}{(version.IsCurrent ? " — vigente" : " — histórica")}";

    public string ZonaNombre(Guid id)
        => Zonas.FirstOrDefault(zone => zone.Id == id)?.Nombre ?? "Zona sin nombre disponible";

    public bool ZonaInactiva(Guid id)
        => Zonas.FirstOrDefault(zone => zone.Id == id)?.Estado == TarifasUi.InactiveZone;

    private async Task<IActionResult> ReloadSelectionsAsync()
    {
        ModelState.Clear();
        var redirect = await LoadOptionsAsync();
        if (redirect is not null) return redirect;
        redirect = await LoadSelectedVersionAsync(clearZone: false);
        return redirect ?? Page();
    }

    private async Task<IActionResult?> LoadOptionsAsync()
    {
        try
        {
            var token = SesionUsuario.ObtenerToken(User);
            var state = await tarifasApi.ObtenerEstadoAsync(token, HttpContext.RequestAborted);
            var publications = await TarifasPageData.CargarTodasLasPublicacionesAsync(tarifasApi, token, HttpContext.RequestAborted);
            var versions = new List<VersionOption>();
            if (state.Borrador is { } draft)
                versions.Add(new VersionOption(draft.VersionTarifarioId, draft, IsDraft: true, IsCurrent: false));
            versions.AddRange(publications
                .DistinctBy(item => item.VersionTarifarioId)
                .OrderByDescending(item => item.NumeroVersionPublicada)
                .Select(item => new VersionOption(item.VersionTarifarioId, item, IsDraft: false,
                    IsCurrent: item.VersionTarifarioId == state.PublicacionVigente?.VersionTarifarioId)));
            Versiones = versions;
            Zonas = await TarifasPageData.CargarTodasLasZonasAsync(zonasApi, token, HttpContext.RequestAborted);
            return null;
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await HandleApiErrorAsync(exception);
            if (redirect is not null) return redirect;
            return null;
        }
        catch (HttpRequestException) { HandleTransportError(); return null; }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { HandleTransportError(); return null; }
    }

    private async Task<IActionResult?> LoadSelectedVersionAsync(bool clearZone)
    {
        if (!Guid.TryParse(VersionTarifarioId, out var id))
        {
            if (!string.IsNullOrWhiteSpace(VersionTarifarioId)) Error ??= "Elegí una versión del tarifario.";
            VersionSeleccionada = null;
            return null;
        }

        var option = Versiones.FirstOrDefault(version => version.Id == id);
        if (option is null)
        {
            Error ??= "La versión no está disponible para este operador. Recargá la página.";
            VersionSeleccionada = null;
            return null;
        }

        try
        {
            var token = SesionUsuario.ObtenerToken(User);
            VersionSeleccionada = option.IsDraft
                ? await tarifasApi.ObtenerBorradorAsync(token, HttpContext.RequestAborted)
                : await tarifasApi.ObtenerPublicacionAsync(id, token, HttpContext.RequestAborted);
            if (clearZone) { ZonaId = ""; Modalidad = ""; }
            if (Guid.TryParse(ZonaId, out var zoneId))
                Modalidades = VersionSeleccionada.Tarifas.Where(rate => rate.ZonaId == zoneId)
                    .Select(rate => rate.Modalidad).Distinct().OrderBy(modality => modality).ToArray();
            return null;
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await HandleApiErrorAsync(exception);
            if (redirect is not null) return redirect;
            VersionSeleccionada = null;
            return null;
        }
        catch (HttpRequestException) { HandleTransportError(); VersionSeleccionada = null; return null; }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            HandleTransportError(); VersionSeleccionada = null; return null;
        }
    }

    private IReadOnlyList<BultoSimulacionRequest> BuildParcels()
    {
        var result = new List<BultoSimulacionRequest>();
        var input = Bultos ?? [];
        if (input.Count is < 1 or > TarifasUi.MaxParcels)
        {
            ModelState.AddModelError(nameof(Bultos), "Agregá entre uno y cien bultos.");
            return result;
        }

        for (var i = 0; i < input.Count; i++)
        {
            var parcel = input[i];
            var weight = ParseMeasure(parcel.PesoGramos, $"Bultos[{i}].PesoGramos", "Peso en gramos");
            var length = ParseMeasure(parcel.LargoCentimetros, $"Bultos[{i}].LargoCentimetros", "Largo en centímetros");
            var width = ParseMeasure(parcel.AnchoCentimetros, $"Bultos[{i}].AnchoCentimetros", "Ancho en centímetros");
            var height = ParseMeasure(parcel.AltoCentimetros, $"Bultos[{i}].AltoCentimetros", "Alto en centímetros");
            if (weight is null || length is null || width is null || height is null) continue;
            result.Add(new BultoSimulacionRequest(weight.Value, length.Value, width.Value, height.Value));
        }
        return result;
    }

    private decimal? ParseMeasure(string? text, string key, string label)
    {
        if (TarifasUi.TryParseNonNegativeDecimal(text, maxFractionDigits: null, out var value) && value > 0) return value;
        ModelState.AddModelError(key, $"Ingresá {label.ToLowerInvariant()} mayor que cero con el separador decimal de tu configuración regional.");
        return null;
    }

    public sealed record VersionOption(Guid Id, VersionTarifarioResumenResponse Version, bool IsDraft, bool IsCurrent);

    public sealed class BultoForm
    {
        public string PesoGramos { get; set; } = "";
        public string LargoCentimetros { get; set; } = "";
        public string AnchoCentimetros { get; set; } = "";
        public string AltoCentimetros { get; set; } = "";
    }
}
