using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.ReglasOperativas.Clients;
using NexoRuta.ApiClient.Features.ReglasOperativas.Contracts;
using NexoRuta.Backoffice.Core.Security;

namespace NexoRuta.Backoffice.Features.ReglasOperativas.Pages;

public sealed class IndexModel(ReglasOperativasApiClient api) : ReglasOperativasPageModel
{
    public ReglasOperativasEstadoResponse? Estado { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            Estado = await api.ObtenerEstadoAsync(SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
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

    public async Task<IActionResult> OnPostCrearBorradorAsync()
    {
        try
        {
            await api.CrearBorradorAsync(SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            return RedirectToPage("/ReglasOperativas/Borrador");
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await HandleApiErrorAsync(exception);
            if (redirect is not null) return redirect;
        }
        catch (HttpRequestException) { HandleTransportError(); }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { HandleTransportError(); }
        try { Estado = await api.ObtenerEstadoAsync(SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted); }
        catch (NexoRutaApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return await ApiSession.RedirectIfExpiredAsync(this, exception) ?? Page();
        }
        catch (NexoRutaApiException) { }
        catch (HttpRequestException) { }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { }
        return Page();
    }
}

public sealed class BorradorModel(ReglasOperativasApiClient api) : ReglasOperativasPageModel
{
    [BindProperty] public string VersionConcurrencia { get; set; } = "";
    [BindProperty] public string? MaxIntentosEntrega { get; set; }
    [BindProperty] public DurationForm IntervaloEntreIntentos { get; set; } = new();
    [BindProperty] public string? PoliticaDevolucionAlAgotarIntentos { get; set; }
    [BindProperty] public DurationForm PlazoEstandar { get; set; } = new();
    [BindProperty] public DurationForm PlazoUrgente { get; set; } = new();
    [BindProperty] public DurationForm PlazoValidacionPrueba { get; set; } = new();
    [BindProperty] public List<string>? RequisitosPrueba { get; set; }
    [BindProperty] public List<MotivoForm>? Motivos { get; set; }
    [BindProperty] public string? ColeccionesEnviadas { get; set; }
    public VersionReglasOperativasResponse? Borrador { get; private set; }
    public bool LocalChangesPending { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            Load(await api.ObtenerBorradorAsync(SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted));
            return Page();
        }
        catch (NexoRutaApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Notice = "Todavía no hay borrador. Creá uno desde el estado de las reglas operativas.";
            return RedirectToPage("/ReglasOperativas/Index");
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

    public IActionResult OnPostAgregarMotivo()
    {
        LocalChangesPending = true;
        Motivos ??= [];
        if (Motivos.Count >= ReglasOperativasUi.MaxMotives)
            Error = $"El borrador admite como máximo {ReglasOperativasUi.MaxMotives} motivos.";
        else
            Motivos.Add(new MotivoForm { EsNuevo = true, Disponible = true });
        ModelState.Clear();
        return Page();
    }

    public IActionResult OnPostQuitarMotivoNuevo(int indice)
    {
        LocalChangesPending = true;
        if (Motivos is not null && indice >= 0 && indice < Motivos.Count && Motivos[indice].EsNuevo)
            Motivos.RemoveAt(indice);
        ModelState.Clear();
        return Page();
    }

    public async Task<IActionResult> OnPostGuardarAsync()
    {
        LocalChangesPending = true;
        if (!long.TryParse(VersionConcurrencia, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion))
            ModelState.AddModelError(nameof(VersionConcurrencia), "Recargá el borrador para obtener una versión válida.");
        if (!string.Equals(ColeccionesEnviadas, "true", StringComparison.Ordinal))
            ModelState.AddModelError(string.Empty, "No se recibieron las listas completas de pruebas y motivos; no se guardó el borrador.");

        var maxAttemptsValid = ReglasOperativasUi.TryOptionalPositiveInt(MaxIntentosEntrega,
            ReglasOperativasUi.MaxAttempts, nameof(MaxIntentosEntrega), ModelState, out var maxAttempts);
        var intervalValid = ReglasOperativasUi.TryDuration(IntervaloEntreIntentos, nameof(IntervaloEntreIntentos), ModelState, out var interval);
        var policyValid = ReglasOperativasUi.TryPolicy(PoliticaDevolucionAlAgotarIntentos,
            nameof(PoliticaDevolucionAlAgotarIntentos), ModelState, out var policy);
        var standardValid = ReglasOperativasUi.TryDuration(PlazoEstandar, nameof(PlazoEstandar), ModelState, out var standard);
        var urgentValid = ReglasOperativasUi.TryDuration(PlazoUrgente, nameof(PlazoUrgente), ModelState, out var urgent);
        var proofValid = ReglasOperativasUi.TryDuration(PlazoValidacionPrueba, nameof(PlazoValidacionPrueba), ModelState, out var proof);
        var proofsValid = TryProofs(out var proofs);
        var motivesValid = TryMotives(out var motives);
        if (!maxAttemptsValid || !intervalValid || !policyValid || !standardValid || !urgentValid || !proofValid || !proofsValid || !motivesValid)
            Error ??= "Revisá los campos señalados. El borrador conserva tus entradas y todavía no se guardó.";
        if (!ModelState.IsValid) return Page();

        var request = new ReemplazarBorradorReglasOperativasRequest(expectedVersion,
            maxAttempts, interval, policy, standard, urgent, proof, proofs, motives);
        try
        {
            await api.ReemplazarBorradorAsync(request, SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            Notice = "El borrador se guardó. Todavía no está publicado.";
            return RedirectToPage("/ReglasOperativas/Borrador");
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await HandleApiErrorAsync(exception);
            if (redirect is not null) return redirect;
            ModelState.AddModelError(string.Empty, Error!);
        }
        catch (HttpRequestException) { HandleTransportError(); }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { HandleTransportError(); }
        return Page();
    }

    private bool TryProofs(out IReadOnlyList<TipoPruebaEntrega> proofs)
    {
        var result = new List<TipoPruebaEntrega>();
        var values = RequisitosPrueba ?? [];
        for (var i = 0; i < values.Count; i++)
        {
            if (!Enum.TryParse<TipoPruebaEntrega>(values[i], ignoreCase: false, out var proof) || !Enum.IsDefined(proof))
                ModelState.AddModelError(nameof(RequisitosPrueba), "Elegí únicamente requisitos de prueba disponibles.");
            else if (result.Contains(proof))
                ModelState.AddModelError(nameof(RequisitosPrueba), "No repitas un requisito de prueba.");
            else result.Add(proof);
        }
        proofs = result;
        return ModelState.IsValid;
    }

    private bool TryMotives(out IReadOnlyList<MotivoNoEntregaRequest> motives)
    {
        var result = new List<MotivoNoEntregaRequest>();
        var input = Motivos ?? [];
        if (input.Count > ReglasOperativasUi.MaxMotives)
            ModelState.AddModelError(nameof(Motivos), $"El borrador admite como máximo {ReglasOperativasUi.MaxMotives} motivos.");
        var codes = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < input.Count; i++)
        {
            var motive = input[i];
            var code = motive.Codigo ?? "";
            var description = motive.Descripcion ?? "";
            if (!Regex.IsMatch(code, "^[A-Z][A-Z0-9_]{0,39}$", RegexOptions.CultureInvariant))
                ModelState.AddModelError($"Motivos[{i}].Codigo", "Usá 1–40 caracteres: una letra A–Z seguida de letras, números o guion bajo.");
            if (!codes.Add(code))
                ModelState.AddModelError($"Motivos[{i}].Codigo", "Cada código de motivo debe ser único.");
            if (string.IsNullOrWhiteSpace(description) || description.Length > 120)
                ModelState.AddModelError($"Motivos[{i}].Descripcion", "Ingresá una descripción de hasta 120 caracteres.");
            result.Add(new MotivoNoEntregaRequest(code, description, motive.Disponible));
        }
        motives = result;
        return ModelState.IsValid;
    }

    private void Load(VersionReglasOperativasResponse draft)
    {
        Borrador = draft;
        VersionConcurrencia = draft.VersionConcurrencia.ToString(CultureInfo.InvariantCulture);
        MaxIntentosEntrega = draft.MaxIntentosEntrega?.ToString(CultureInfo.InvariantCulture);
        IntervaloEntreIntentos = ReglasOperativasUi.Duration(draft.IntervaloEntreIntentosMinutos);
        PoliticaDevolucionAlAgotarIntentos = draft.PoliticaDevolucionAlAgotarIntentos?.ToString();
        PlazoEstandar = ReglasOperativasUi.Duration(draft.PlazoEstandarMinutos);
        PlazoUrgente = ReglasOperativasUi.Duration(draft.PlazoUrgenteMinutos);
        PlazoValidacionPrueba = ReglasOperativasUi.Duration(draft.PlazoValidacionPruebaMinutos);
        RequisitosPrueba = draft.RequisitosPrueba.Select(value => value.ToString()).ToList();
        Motivos = draft.Motivos.Select(value => new MotivoForm
        {
            Codigo = value.Codigo, Descripcion = value.Descripcion, Disponible = value.Disponible, EsNuevo = false
        }).ToList();
        ColeccionesEnviadas = "true";
    }
}

public sealed class PublicarModel(ReglasOperativasApiClient api) : ReglasOperativasPageModel
{
    [BindProperty] public string VersionConcurrencia { get; set; } = "";
    [BindProperty] public bool ConfirmarPublicacion { get; set; }
    public VersionReglasOperativasResponse? Borrador { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            Borrador = await api.ObtenerBorradorAsync(SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            VersionConcurrencia = Borrador.VersionConcurrencia.ToString(CultureInfo.InvariantCulture);
            return Page();
        }
        catch (NexoRutaApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Notice = "No hay un borrador para publicar.";
            return RedirectToPage("/ReglasOperativas/Index");
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

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ConfirmarPublicacion)
            ModelState.AddModelError(nameof(ConfirmarPublicacion), "Confirmá que querés publicar estas reglas como una versión inmutable.");
        if (!long.TryParse(VersionConcurrencia, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion))
            ModelState.AddModelError(nameof(VersionConcurrencia), "Recargá el borrador antes de publicar.");
        if (!ModelState.IsValid)
        {
            var loadRedirect = await LoadDraftForSummaryAsync();
            if (loadRedirect is not null) return loadRedirect;
            return Page();
        }
        try
        {
            var published = await api.PublicarBorradorAsync(new PublicarBorradorReglasOperativasRequest(expectedVersion),
                SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            Notice = $"Se publicaron las reglas, versión {published.NumeroVersionPublicada}.";
            return RedirectToPage("/ReglasOperativas/Publicaciones/Detalle", new { versionId = published.VersionReglasOperativasId });
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await HandleApiErrorAsync(exception);
            if (redirect is not null) return redirect;
            ModelState.AddModelError(string.Empty, Error!);
        }
        catch (HttpRequestException) { HandleTransportError(); }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { HandleTransportError(); }
        var draftRedirect = await LoadDraftForSummaryAsync();
        if (draftRedirect is not null) return draftRedirect;
        return Page();
    }

    private async Task<IActionResult?> LoadDraftForSummaryAsync()
    {
        try { Borrador = await api.ObtenerBorradorAsync(SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted); }
        catch (NexoRutaApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return await ApiSession.RedirectIfExpiredAsync(this, exception);
        }
        catch (NexoRutaApiException) { }
        catch (HttpRequestException) { }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { }
        return null;
    }

    public IReadOnlyList<string> MissingSections()
    {
        if (Borrador is not { } draft) return [];
        var missing = new List<string>();
        if (draft.MaxIntentosEntrega is null || draft.IntervaloEntreIntentosMinutos is null) missing.Add("Intentos de entrega");
        if (draft.PoliticaDevolucionAlAgotarIntentos is null) missing.Add("Política cuando se agotan los intentos");
        if (draft.PlazoEstandarMinutos is null || draft.PlazoUrgenteMinutos is null) missing.Add("Plazos de entrega");
        if (draft.PlazoValidacionPruebaMinutos is null) missing.Add("Plazo de validación de prueba");
        if (draft.RequisitosPrueba.Count == 0) missing.Add("Prueba requerida para entregar");
        if (!draft.Motivos.Any(motive => motive.Disponible)) missing.Add("Motivos de no entrega disponibles");
        return missing;
    }
}

public sealed class PublicacionesIndexModel(ReglasOperativasApiClient api) : ReglasOperativasPageModel
{
    [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;
    public PaginaPublicacionesReglasOperativasResponse? Resultado { get; private set; }
    public Guid? VersionVigenteId { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (Pagina < 1) Pagina = 1;
        try
        {
            var token = SesionUsuario.ObtenerToken(User);
            Resultado = await api.ListarPublicacionesAsync(token, Pagina, ReglasOperativasUi.DefaultHistoryPageSize, HttpContext.RequestAborted);
            var state = await api.ObtenerEstadoAsync(token, HttpContext.RequestAborted);
            VersionVigenteId = state.PublicacionVigente?.VersionReglasOperativasId;
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
}

public sealed class PublicacionDetalleModel(ReglasOperativasApiClient api) : ReglasOperativasPageModel
{
    [BindProperty(SupportsGet = true)] public Guid VersionId { get; set; }
    public VersionReglasOperativasResponse? Publicacion { get; private set; }
    public bool HayBorrador { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (VersionId == Guid.Empty) return NotFound();
        try
        {
            var token = SesionUsuario.ObtenerToken(User);
            Publicacion = await api.ObtenerPublicacionAsync(VersionId, token, HttpContext.RequestAborted);
            var state = await api.ObtenerEstadoAsync(token, HttpContext.RequestAborted);
            HayBorrador = state.Borrador is not null;
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
}
