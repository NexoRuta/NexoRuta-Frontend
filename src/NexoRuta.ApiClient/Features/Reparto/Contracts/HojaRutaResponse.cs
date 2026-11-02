namespace NexoRuta.ApiClient.Features.Reparto.Contracts;

public sealed record HojaRutaResponse(Guid Id, Guid JornadaId, Guid RepartidorAccesoId, Guid VehiculoId,
    string Matricula, DateOnly Fecha, string Estado, long Version, long UltimaSecuencia,
    ConfiguracionRepartoResponse Politica, IReadOnlyList<ParadaRutaResponse> Paradas,
    IReadOnlyList<IncidenciaCierreResponse> IncidenciasCierre, bool? ReservaRecursosActiva = null, PlanTemporalResponse? PlanTemporal = null, string EstadoEstimacion = "SinEstimacion", string MotivoSinEstimacion = "Datos temporales no configurados", DateTimeOffset? SalidaEnUtc = null);
public sealed record ParadaRutaResponse(Guid EnvioId, int Orden, bool Activa, string? MotivoSuspension,
    DateTimeOffset LlegadaEstimadaUtc, string Estado, long EnvioVersion, string Destinatario,
    string Direccion, double Latitud, double Longitud, IReadOnlyList<BultoRutaResponse> Bultos, ReglasEnvioResponse Reglas,
    int IntentosFallidos, DateTimeOffset? ProximoIntentoElegibleUtc, int? ServicioMinutos = null);
public sealed record BultoRutaResponse(Guid Id, string Codigo, decimal PesoGramos, decimal VolumenCm3, string Custodia, long Version);
public sealed record PlanificarRutaRequest(Guid RepartidorAccesoId, Guid VehiculoId, DateOnly Fecha,
    IReadOnlyList<ParadaPlanificadaRequest> Paradas, DateTimeOffset? SalidaPlanificadaEnUtc = null);
public sealed record ParadaPlanificadaRequest(Guid EnvioId, DateTimeOffset LlegadaEstimadaUtc, int? ServicioMinutos = null);
public sealed record ReasignarEnvioRequest(Guid RutaDestinoId, long VersionEnvio, long VersionRutaDestino,
    DateTimeOffset LlegadaEstimadaUtc, string Motivo, int? ServicioMinutos = null);
public sealed record ReprogramacionEnvioResponse(Guid EnvioId, long VersionEnvio, int IntentosFallidos,
    int MaximoIntentos, DateTimeOffset? ProximoIntentoElegibleUtc);
public sealed record RecursosPlanificacionResponse(ConfiguracionRepartoResponse Politica,
    IReadOnlyList<RepartidorPlanificacionResponse> Repartidores, IReadOnlyList<VehiculoResponse> Vehiculos, BaseOperativaResponse? BaseOperativa = null);
public sealed record RepartidorPlanificacionResponse(Guid AccesoId, string Nombre);
