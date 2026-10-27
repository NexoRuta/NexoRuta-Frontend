using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.ApiClient.Features.Envios.Contracts.Responses;
using NexoRuta.ApiClient.Features.Reparto.Clients;
using NexoRuta.ApiClient.Features.Reparto.Contracts;
using NexoRuta.ApiClient.Features.Usuarios.Clients;
using NexoRuta.Backoffice.Core.Security;
namespace NexoRuta.Backoffice.Features.Reparto.Pages;

[RequestFormLimits(ValueCountLimit = 4000)]
public sealed class RutasModel(RutasApiClient api, EnviosApiClient envios, UsuariosApiClient usuarios) : PageModel
{
    private const string ZonaHorariaPredeterminada = "America/Montevideo";
    [BindProperty(SupportsGet = true)] public DateOnly Fecha { get; set; }
    [BindProperty] public Guid RepartidorAccesoId { get; set; }
    [BindProperty] public Guid VehiculoId { get; set; }
    [BindProperty] public Guid DevolucionEnvioId { get; set; }
    [BindProperty] public long VersionEnvio { get; set; }
    [BindProperty] public Guid ComandoId { get; set; }
    [BindProperty] public string? MotivoDevolucion { get; set; }
    [BindProperty] public bool ConfirmarDevolucion { get; set; }
    [BindProperty] public Guid ExtravioEnvioId { get; set; }
    [BindProperty] public string? MotivoExtravio { get; set; }
    [BindProperty] public bool ConfirmarExtravio { get; set; }
    [BindProperty] public bool CrearBorrador { get; set; }
    [BindProperty] public List<ParadaFormulario> Plan { get; set; } = [];
    public IReadOnlyList<HojaRutaResponse> Rutas { get; private set; } = [];
    public RecursosPlanificacionResponse? Recursos { get; private set; }
    public IReadOnlyList<EnvioResponse> Envios { get; private set; } = [];
    public bool CargaCompleta { get; private set; }
    public bool PuedeGestionarDecisionesOperativasEnvio { get; private set; }
    public string? Error { get; private set; }
    [TempData] public string? Confirmacion { get; set; }
    public Task<IActionResult> OnGetAsync() => Cargar();
    public Task<IActionResult> OnGetReintentarAsync() => Cargar();
    public Task<IActionResult> OnPostPlanificarAsync() => Ejecutar(async () =>
    {
        if (!ModelState.IsValid) throw new ArgumentException("Revisá los campos de la planificación.");
        var elegidas = Plan.Where(p => p.Incluir).ToArray();
        if (elegidas.Length == 0 && !CrearBorrador || elegidas.Length > 500 || elegidas.Any(p => p.EnvioId == Guid.Empty || p.Orden is null or < 1 || p.Hora is null)
            || elegidas.Select(p => p.Orden).Distinct().Count() != elegidas.Length)
            throw new ArgumentException("Seleccioná envíos y completá un orden único y una hora para cada parada.");
        var recursos = await api.RecursosAsync(Token(), HttpContext.RequestAborted);
        var paradas = elegidas.OrderBy(p => p.Orden).Select(p => new ParadaPlanificadaRequest(p.EnvioId,
            ConvertirLlegada(Fecha, p.Hora!.Value, recursos.Politica.ZonaHoraria))).ToArray();
        await api.PlanificarAsync(new(RepartidorAccesoId, VehiculoId, Fecha, paradas), Token(), HttpContext.RequestAborted);
        Confirmacion = "Ruta planificada. Revisá sus paradas y la recepción física antes de despachar.";
    });
    public Task<IActionResult> OnPostDespacharAsync(Guid rutaId, long version) => Ejecutar(async () =>
    {
        await api.DespacharAsync(rutaId, version, Token(), HttpContext.RequestAborted);
        Confirmacion = "Ruta despachada; el repartidor ya puede descargar su hoja.";
    });
    public Task<IActionResult> OnPostRecibirAsync(Guid envioId, long version) => Ejecutar(async () =>
    {
        await api.RecibirAsync(envioId, version, Token(), HttpContext.RequestAborted);
        Confirmacion = "Recepción de los bultos confirmada en depósito.";
    });
    public Task<IActionResult> OnPostReasignarAsync(Guid envioId, long versionEnvio, string? destino, TimeOnly? hora, string? motivo) => Ejecutar(async () =>
    {
        var partes = destino?.Split(':');
        if (partes?.Length != 2 || !Guid.TryParse(partes[0], out var rutaId) || !long.TryParse(partes[1], NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            || hora is null || string.IsNullOrWhiteSpace(motivo) || motivo.Length > 500)
            throw new ArgumentException("Elegí una ruta planificada, la hora de llegada y el motivo de la postergación.");
        var ruta = (await api.ListarAsync(Fecha, Token(), HttpContext.RequestAborted)).SingleOrDefault(r => r.Id == rutaId);
        if (ruta is null || ruta.Estado != "Planificada") throw new ArgumentException("La ruta destino ya no está disponible. Actualizá la fecha y volvé a elegirla.");
        await api.ReasignarAsync(envioId, new(rutaId, versionEnvio, version,
            ConvertirLlegada(ruta.Fecha, hora.Value, ruta.Politica.ZonaHoraria), motivo), Token(), HttpContext.RequestAborted);
        Confirmacion = "Envío postergado. Si hay un repartidor en reparto, tiene un aviso; el bulto debe regresar al depósito antes del nuevo despacho.";
    });
    public Task<IActionResult> OnPostReprogramarAsync(Guid envioId, long versionEnvio) => Ejecutar(async () =>
    {
        var resultado = await api.ReprogramarAsync(envioId, versionEnvio, Token(), HttpContext.RequestAborted);
        var proximo = resultado.ProximoIntentoElegibleUtc is { } utc
            ? $" La próxima tentativa es elegible desde {utc:yyyy-MM-dd HH:mm} UTC."
            : " No se pudo determinar una próxima elegibilidad temporal.";
        Confirmacion = $"Envío reprogramado para otra ruta; se conservan los {resultado.IntentosFallidos} intentos fallidos.{proximo}";
    });
    public async Task<IActionResult> OnPostIniciarDevolucionAsync()
    {
        try
        {
            var motivo = MotivoDevolucion?.Trim();
            if (!ModelState.IsValid || DevolucionEnvioId == Guid.Empty || VersionEnvio < 0 || ComandoId == Guid.Empty
                || string.IsNullOrWhiteSpace(motivo) || motivo.Length > 500 || !ConfirmarDevolucion)
                throw new ArgumentException("Indicá un motivo de hasta 500 caracteres y confirmá el inicio de la devolución.");

            var resultado = await api.IniciarDevolucionAsync(DevolucionEnvioId,
                new(VersionEnvio, ComandoId, motivo), Token(), HttpContext.RequestAborted);
            Confirmacion = resultado.EstadoActual == "EnDevolucion"
                ? "Devolución iniciada. La recepción del bulto en depósito se confirmará por separado."
                : $"La decisión ya estaba aplicada; el estado actual del envío es {resultado.EstadoActual}. No se modificó nuevamente.";
            return RedirectToPage(new { fecha = Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
        }
        catch (NexoRutaApiException ex)
        {
            var redir = await ApiSession.RedirectIfExpiredAsync(this, ex);
            if (redir is not null) return redir;
            Error = ex.StatusCode is { } status && (int)status >= 500
                ? "No se recibió confirmación. La decisión podría haberse guardado; podés reintentar sin cambiar el motivo ni el comando."
                : ex.Message;
        }
        catch (ArgumentException ex) { Error = ex.Message; }
        catch (HttpRequestException)
        {
            Error = "No se recibió la respuesta. La decisión podría haberse guardado; podés reintentar sin cambiar el motivo ni el comando.";
        }
        catch (TaskCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            Error = "Se agotó el tiempo de espera. La decisión podría haberse guardado; podés reintentar sin cambiar el motivo ni el comando.";
        }
        return await Cargar();
    }
    public async Task<IActionResult> OnPostConfirmarExtravioAsync()
    {
        try
        {
            var motivo = MotivoExtravio?.Trim();
            if (!ModelState.IsValid || ExtravioEnvioId == Guid.Empty || VersionEnvio < 0 || ComandoId == Guid.Empty
                || string.IsNullOrWhiteSpace(motivo) || motivo.Length > 500 || !ConfirmarExtravio)
                throw new ArgumentException("Indicá un motivo de hasta 500 caracteres y confirmá que el envío pasará a Extraviado.");

            var resultado = await api.ConfirmarExtravioAsync(ExtravioEnvioId,
                new(VersionEnvio, ComandoId, motivo), Token(), HttpContext.RequestAborted);
            Confirmacion = resultado.Repetida
                ? $"La confirmación ya estaba aplicada. El estado actual es {resultado.EstadoActual}; no se duplicaron sus efectos. La custodia conserva únicamente el último registro conocido."
                : "Extravío confirmado. No se registró una entrega ni un movimiento físico; la custodia conserva únicamente el último registro conocido.";
            return RedirectToPage(new { fecha = Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
        }
        catch (NexoRutaApiException ex)
        {
            var redir = await ApiSession.RedirectIfExpiredAsync(this, ex);
            if (redir is not null) return redir;
            if ((int?)ex.StatusCode == 409)
            {
                ComandoId = Guid.Empty;
                ConfirmarExtravio = false;
                Error = $"{ex.Message} Se actualizó el estado; revisá la información antes de confirmar de nuevo.";
            }
            else
                Error = ex.StatusCode is { } status && (int)status >= 500
                    ? "No se recibió confirmación. El extravío podría haberse guardado; reintentá sin cambiar el motivo ni el comando."
                    : ex.Message;
        }
        catch (ArgumentException ex) { Error = ex.Message; }
        catch (HttpRequestException)
        {
            Error = "No se recibió la respuesta. El extravío podría haberse guardado; reintentá sin cambiar el motivo ni el comando.";
        }
        catch (TaskCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            Error = "Se agotó el tiempo de espera. El extravío podría haberse guardado; reintentá sin cambiar el motivo ni el comando.";
        }
        return await Cargar();
    }
    private async Task<IActionResult> Ejecutar(Func<Task> accion)
    {
        try
        {
            if (!ModelState.IsValid) throw new ArgumentException("Revisá los valores del formulario.");
            await accion(); return RedirectToPage(new { fecha = Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
        }
        catch (NexoRutaApiException ex) { var redir = await ApiSession.RedirectIfExpiredAsync(this, ex); if (redir is not null) return redir; Error = ex.Message; }
        catch (ArgumentException ex) { Error = ex.Message; }
        catch (HttpRequestException) { Error = "No se pudo conectar; actualizá el estado antes de repetir la operación."; }
        return await Cargar();
    }
    private async Task<IActionResult> Cargar()
    {
        CargaCompleta = false;
        var inicializarFecha = Fecha == default;
        if (inicializarFecha) Fecha = FechaActual(ZonaHorariaPredeterminada);
        try
        {
            var recursos = await api.RecursosAsync(Token(), HttpContext.RequestAborted);
            if (inicializarFecha) Fecha = FechaActual(recursos.Politica.ZonaHoraria);
            var rutas = await api.ListarAsync(Fecha, Token(), HttpContext.RequestAborted);
            var enviosActuales = await envios.ListarEnviosAsync(Token(), HttpContext.RequestAborted);
            var usuarioActual = await usuarios.ObtenerUsuarioActualAsync(Token(), HttpContext.RequestAborted);
            var anteriores = Plan.GroupBy(p => p.EnvioId).ToDictionary(g => g.Key, g => g.First());
            var plan = enviosActuales.Where(e => e.Estado is "EnDeposito" or "Reprogramado").Select(e => anteriores.GetValueOrDefault(e.Id) ?? new() { EnvioId = e.Id }).ToList();
            Recursos = recursos;
            Rutas = rutas;
            Envios = enviosActuales;
            PuedeGestionarDecisionesOperativasEnvio = usuarioActual.PuedeGestionarDecisionesOperativasEnvio;
            Plan = plan;
            CargaCompleta = true;
        }
        catch (NexoRutaApiException ex) { var redir = await ApiSession.RedirectIfExpiredAsync(this, ex); if (redir is not null) return redir; Error ??= ex.Message; }
        catch (HttpRequestException) { Error ??= "No se pudo consultar el servicio de reparto."; }
        return Page();
    }
    private static DateOnly FechaActual(string zonaHoraria)
    {
        try
        {
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById(zonaHoraria)).DateTime);
        }
        catch (TimeZoneNotFoundException)
        {
            return DateOnly.FromDateTime(DateTime.Now);
        }
        catch (InvalidTimeZoneException)
        {
            return DateOnly.FromDateTime(DateTime.Now);
        }
    }
    public static DateTimeOffset ConvertirLlegada(DateOnly fecha, TimeOnly hora, string zonaHoraria)
    {
        var local = fecha.ToDateTime(hora, DateTimeKind.Unspecified);
        var zona = TimeZoneInfo.FindSystemTimeZoneById(zonaHoraria);
        if (zona.IsInvalidTime(local) || zona.IsAmbiguousTime(local)) throw new ArgumentException("La hora elegida es ambigua o no existe por un cambio horario. Elegí otra hora.");
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zona));
    }
    public string Repartidor(Guid acceso) => Recursos?.Repartidores.SingleOrDefault(r => r.AccesoId == acceso)?.Nombre ?? "Repartidor deshabilitado";
    public string Hora(DateTimeOffset utc, string zona) => TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById(zona)).ToString("HH:mm", CultureInfo.InvariantCulture);
    public string FechaHora(DateTimeOffset utc, string zona) => TimeZoneInfo.ConvertTime(utc,
        TimeZoneInfo.FindSystemTimeZoneById(zona)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    private string Token() => SesionUsuario.ObtenerToken(User);
    public sealed class ParadaFormulario
    {
        public Guid EnvioId { get; set; }
        public bool Incluir { get; set; }
        [Range(1, 500)] public int? Orden { get; set; }
        public TimeOnly? Hora { get; set; }
    }
}
