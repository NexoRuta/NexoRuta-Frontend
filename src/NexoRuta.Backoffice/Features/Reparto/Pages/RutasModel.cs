using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
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
public sealed class RutasModel(RutasApiClient api, EnviosApiClient envios, UsuariosApiClient usuarios,
    IAuthorizationService authorization) : PageModel
{
    private const string ZonaHorariaPredeterminada = "America/Montevideo";
    [BindProperty(SupportsGet = true)] public DateOnly Fecha { get; set; }
    [BindProperty] public string? FechaSalida { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? RutaSeleccionadaId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Pestana { get; set; }
    [BindProperty(SupportsGet = true)] public string? BuscarRuta { get; set; }
    [BindProperty(SupportsGet = true)] public string? FiltroRuta { get; set; }
    [BindProperty(SupportsGet = true)] public string? BuscarRecepcion { get; set; }
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
    public IReadOnlyList<HojaRutaResponse> RutasReserva => Rutas.Where(r => r.Fecha == Fecha
        && ConservaReservaDeRecursos(r)
        && (r.RepartidorAccesoId == RepartidorAccesoId || r.VehiculoId == VehiculoId)
        ).ToArray();
    public RecursosPlanificacionResponse? Recursos { get; private set; }
    public IReadOnlyList<EnvioResponse> Envios { get; private set; } = [];
    public bool CargaCompleta { get; private set; }
    public bool PuedeConsultarEnvios { get; private set; }
    public bool PuedeGestionarRutas { get; private set; }
    public bool PuedeGestionarDecisionesOperativasEnvio { get; private set; }
    public string? Error { get; private set; }
    [TempData] public string? Confirmacion { get; set; }
    public string ZonaHoraria => Recursos?.Politica.ZonaHoraria ?? ZonaHorariaPredeterminada;
    public DateOnly HoyOperativo { get; private set; }
    public IReadOnlyList<HojaRutaResponse> RutasVisibles => Rutas
        .Where(r => string.IsNullOrWhiteSpace(BuscarRuta) ||
            Repartidor(r.RepartidorAccesoId).Contains(BuscarRuta, StringComparison.CurrentCultureIgnoreCase) ||
            r.Matricula.Contains(BuscarRuta, StringComparison.CurrentCultureIgnoreCase) ||
            r.Estado.Contains(BuscarRuta, StringComparison.CurrentCultureIgnoreCase))
        .Where(r => string.IsNullOrWhiteSpace(FiltroRuta) || FiltroRuta == "Todas" ||
            (FiltroRuta == "Activas" && r.Estado is "Planificada" or "EnCurso") ||
            (FiltroRuta == "Despachada" && r.Estado is "Despachada" or "EnCurso") || r.Estado == FiltroRuta)
        .OrderBy(r => r.Estado == "EnCurso" ? 0 : 1).ThenBy(r => r.Matricula, StringComparer.CurrentCultureIgnoreCase).ToArray();
    public HojaRutaResponse? RutaSeleccionada => RutasVisibles.FirstOrDefault(r => r.Id == RutaSeleccionadaId)
        ?? RutasVisibles.FirstOrDefault();
    public IReadOnlyList<EnvioResponse> RecepcionesPendientes => Envios
        .Where(AdmiteRecepcion)
        .Where(e => string.IsNullOrWhiteSpace(BuscarRecepcion) ||
            e.DestinatarioNombre.Contains(BuscarRecepcion, StringComparison.CurrentCultureIgnoreCase) ||
            e.Direccion.Contains(BuscarRecepcion, StringComparison.CurrentCultureIgnoreCase) ||
            e.Bultos.Any(b => b.Codigo.Contains(BuscarRecepcion, StringComparison.CurrentCultureIgnoreCase)))
        .OrderBy(e => e.DestinatarioNombre, StringComparer.CurrentCultureIgnoreCase).ToArray();
    public Task<IActionResult> OnGetAsync() => Cargar();
    public Task<IActionResult> OnGetReintentarAsync() => Cargar();
    public Task<IActionResult> OnPostPlanificarAsync() => PlanificarConFechaSeleccionada();
    private async Task<IActionResult> PlanificarConFechaSeleccionada()
    {
        if (!DateOnly.TryParseExact(FechaSalida, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fechaSeleccionada)
            || fechaSeleccionada == default)
        {
            Fecha = default;
            ModelState.AddModelError(nameof(FechaSalida), "Seleccioná una fecha de salida válida.");
            Error = "La fecha de salida no es válida. Seleccioná una fecha para continuar.";
            return await Cargar();
        }
        Fecha = fechaSeleccionada;
        return await Ejecutar(async () =>
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
    }
    public Task<IActionResult> OnPostDespacharAsync(Guid rutaId, long version) => Ejecutar(async () =>
    {
        await api.DespacharAsync(rutaId, version, Token(), HttpContext.RequestAborted);
        Confirmacion = "Ruta despachada; el repartidor ya puede descargar su hoja.";
    });
    public async Task<IActionResult> OnPostCancelarRutaAsync(Guid rutaId, long version)
    {
        if (!(await authorization.AuthorizeAsync(User, resource: null, "envios.gestionar")).Succeeded) return Forbid();
        try
        {
            if (rutaId == Guid.Empty || version < 0) throw new ArgumentException("La ruta o su versión no son válidas.");
            var ruta = await api.CancelarRutaAsync(rutaId, version, Token(), HttpContext.RequestAborted);
            RutaSeleccionadaId = ruta.Id;
            Pestana = "rutas";
            Confirmacion = "Ruta cancelada. Se liberaron repartidor y vehículo; el historial y las paradas suspendidas se conservaron.";
            return RedirectarDiario();
        }
        catch (NexoRutaApiException ex)
        {
            var redir = await ApiSession.RedirectIfExpiredAsync(this, ex);
            if (redir is not null) return redir;
            Error = (int?)ex.StatusCode == 409
                ? $"{ex.Message} Se actualizó la ruta; revisá su estado antes de volver a actuar."
                : ex.Message;
        }
        catch (ArgumentException ex) { Error = ex.Message; }
        catch (HttpRequestException) { Error = "No se pudo confirmar el resultado. Actualizá la ruta antes de repetir la operación."; }
        return await Cargar();
    }
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
        Confirmacion = "Ruta reasignada. La custodia física no cambió; el despacho requiere recepción efectiva en depósito.";
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
            return RedirectarDiario();
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
            return RedirectarDiario();
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
            await accion(); return RedirectarDiario();
        }
        catch (NexoRutaApiException ex) { var redir = await ApiSession.RedirectIfExpiredAsync(this, ex); if (redir is not null) return redir; Error = ex.Message; }
        catch (ArgumentException ex) { Error = ex.Message; }
        catch (HttpRequestException) { Error = "No se pudo conectar; actualizá el estado antes de repetir la operación."; }
        return await Cargar();
    }
    private async Task<IActionResult> Cargar()
    {
        CargaCompleta = false;
        var fechaInvalida = ModelState.TryGetValue(nameof(FechaSalida), out var fechaSalidaModelState) && fechaSalidaModelState.Errors.Count > 0
            || ModelState.TryGetValue(nameof(Fecha), out var fechaModelState) && fechaModelState.Errors.Count > 0;
        var inicializarFecha = Fecha == default && !fechaInvalida;
        if (inicializarFecha) Fecha = FechaActual(ZonaHorariaPredeterminada);
        if (fechaInvalida) Error ??= "La fecha de salida no es válida. Seleccioná una fecha para continuar.";
        try
        {
            var recursos = await api.RecursosAsync(Token(), HttpContext.RequestAborted);
            if (inicializarFecha) Fecha = FechaActual(recursos.Politica.ZonaHoraria);
            var rutas = fechaInvalida ? Array.Empty<HojaRutaResponse>() : await api.ListarAsync(Fecha, Token(), HttpContext.RequestAborted);
            PuedeConsultarEnvios = (await authorization.AuthorizeAsync(User, resource: null, "envios.consultar")).Succeeded;
            PuedeGestionarRutas = (await authorization.AuthorizeAsync(User, resource: null, "envios.gestionar")).Succeeded;
            var enviosActuales = PuedeConsultarEnvios
                ? await envios.ListarEnviosAsync(Token(), HttpContext.RequestAborted)
                : Array.Empty<EnvioResponse>();
            var usuarioActual = await usuarios.ObtenerUsuarioActualAsync(Token(), HttpContext.RequestAborted);
            var anteriores = Plan.GroupBy(p => p.EnvioId).ToDictionary(g => g.Key, g => g.First());
            var plan = enviosActuales.Where(e => e.Estado is "EnDeposito" or "Reprogramado").Select(e => anteriores.GetValueOrDefault(e.Id) ?? new() { EnvioId = e.Id }).ToList();
            Recursos = recursos;
            Rutas = rutas;
            Envios = enviosActuales;
            HoyOperativo = FechaActual(recursos.Politica.ZonaHoraria);
            PuedeGestionarDecisionesOperativasEnvio = usuarioActual.PuedeGestionarDecisionesOperativasEnvio;
            Plan = plan;
            FechaSalida ??= fechaInvalida ? string.Empty : Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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
    public string RecursosReservados(HojaRutaResponse ruta)
    {
        var recursos = new List<string>();
        if (ruta.RepartidorAccesoId == RepartidorAccesoId) recursos.Add($"repartidor {Repartidor(ruta.RepartidorAccesoId)}");
        if (ruta.VehiculoId == VehiculoId) recursos.Add($"vehículo {ruta.Matricula}");
        return string.Join(" y ", recursos);
    }
    public string InicialesRepartidor(Guid acceso)
    {
        var partes = Repartidor(acceso).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(partes.Take(2).Select(parte => char.ToUpperInvariant(parte[0])));
    }
    public bool CustodiaEnDeposito(Guid envioId)
    {
        var bultosEnvio = Envios.SingleOrDefault(e => e.Id == envioId)?.Bultos;
        if (bultosEnvio is null || bultosEnvio.Count == 0) return false;
        return bultosEnvio.All(b => b.Custodia == "Deposito");
    }
    public bool ConservaReservaDeRecursos(HojaRutaResponse ruta) => ruta.Estado is not ("Finalizada" or "Cancelada")
        && (ruta.ReservaRecursosActiva ?? true);
    public bool PuedeRecibirReasignaciones(HojaRutaResponse ruta) => ruta.Estado == "Planificada"
        && ConservaReservaDeRecursos(ruta);
    public bool PuedeCancelarRuta(HojaRutaResponse ruta) => PuedeGestionarRutas
        && ruta.Estado == "Planificada" && ruta.Paradas.All(p => !p.Activa);
    public bool DestinoConfirmado(Guid envioId) => Envios.SingleOrDefault(e => e.Id == envioId)?.DestinoConfirmado == true;
    public bool AdmiteRecepcion(EnvioResponse envio) =>
        envio.Estado is "Admitido" or "Reprogramado" or "Cancelado" or "AsignadoRuta" or "EnDevolucion" ||
        Rutas.Any(r => r.IncidenciasCierre.Any(i => i.EnvioId == envio.Id)) && envio.Estado is "EnTransito" or "NoEntregado";
    public bool EsRecepcionInicial(EnvioResponse envio) => envio.Estado == "Admitido";
    public bool EsRecepcionDevolucion(EnvioResponse envio) => envio.Estado == "EnDevolucion";
    public string ResumenRecepcion(EnvioResponse envio) => envio.Estado switch
    {
        "Admitido" => "Primera recepción del envío en depósito",
        "EnDevolucion" => "Recepción física de una devolución iniciada",
        "Cancelado" => "Registrar custodia física sin reactivar el estado Cancelado",
        "AsignadoRuta" => "Confirmar custodia en depósito antes de despachar o reasignar",
        "EnTransito" or "NoEntregado" => "Recepción vinculada a una incidencia de cierre de jornada",
        _ => "Confirmar la custodia física en depósito"
    };
    public string FechaLegible(DateOnly fecha) => fecha.ToDateTime(TimeOnly.MinValue)
        .ToString("dddd, d 'de' MMMM 'de' yyyy", CultureInfo.GetCultureInfo("es-UY"));
    public string RutaQuery(Guid? id) => id is null ? string.Empty : id.Value.ToString();
    public string EstadoRuta(string estado) => estado switch
    {
        "Planificada" => "Planificada",
        "EnCurso" => "En curso",
        "Despachada" => "Despachada",
        "Finalizada" => "Finalizada",
        "Cancelada" => "Cancelada",
        _ => estado
    };
    public string EtiquetaEstadoEnvio(string estado) => estado switch
    {
        "EnDeposito" => "En depósito",
        "AsignadoRuta" => "Asignado a ruta",
        "EnTransito" => "En tránsito",
        "EntregaPendienteValidacion" => "Entrega pendiente de validación",
        "EntregaSinValidacion" => "Entrega sin validación",
        "EnDevolucion" => "En devolución",
        _ => estado
    };
    public string EtiquetaCustodia(string custodia) => custodia switch
    {
        "SinRecepcion" => "Sin recepción",
        "PendienteDeposito" => "Pendiente de depósito",
        "Deposito" => "En depósito",
        "Repartidor" => "En custodia del repartidor",
        _ => custodia
    };
    public string Hora(DateTimeOffset utc, string zona) => TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById(zona)).ToString("HH:mm", CultureInfo.InvariantCulture);
    public string FechaHora(DateTimeOffset utc, string zona) => TimeZoneInfo.ConvertTime(utc,
        TimeZoneInfo.FindSystemTimeZoneById(zona)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    private string Token() => SesionUsuario.ObtenerToken(User);
    private IActionResult RedirectarDiario() => RedirectToPage("/Rutas", new
    {
        fecha = Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        pestana = string.IsNullOrWhiteSpace(Pestana) ? "rutas" : Pestana,
        rutaSeleccionadaId = RutaSeleccionadaId,
        buscarRuta = BuscarRuta,
        filtroRuta = FiltroRuta,
        buscarRecepcion = BuscarRecepcion
    });
    public sealed class ParadaFormulario
    {
        public Guid EnvioId { get; set; }
        public bool Incluir { get; set; }
        [Range(1, 500)] public int? Orden { get; set; }
        public TimeOnly? Hora { get; set; }
    }
}
