using System.ComponentModel.DataAnnotations;
namespace NexoRuta.ApiClient.Features.Reparto.Contracts;
public sealed record BaseOperativaResponse(long Version, string Direccion, double? Latitud, double? Longitud,
    DateTimeOffset? ConfirmadaEnUtc, Guid? ConfirmadaPorAccesoId, int? ServicioPredeterminadoMinutos, int? MargenRetornoMinutos);
public sealed record GuardarBaseOperativaRequest(long Version, string Direccion, int? ServicioPredeterminadoMinutos, int? MargenRetornoMinutos);
public sealed record ConfirmarBaseOperativaRequest(long Version, double Latitud, double Longitud);
public sealed record PropuestaBaseResponse(string Descripcion, double Latitud, double Longitud);
public sealed record PropuestasBaseResponse(long Version, string Direccion, string Atribucion, IReadOnlyList<PropuestaBaseResponse> Propuestas);
public sealed record PlanTemporalResponse(DateTimeOffset? SalidaPlanificadaEnUtc, string ZonaHoraria, long? BaseVersion,
    string? DireccionBase, double? LatitudBase, double? LongitudBase, DateTimeOffset? BaseConfirmadaEnUtc,
    Guid? BaseConfirmadaPorAccesoId, int? ServicioPredeterminadoMinutos, int? MargenRetornoMinutos, long VersionRutaAlPlanificar, IReadOnlyList<Guid> OrdenEnvios);
