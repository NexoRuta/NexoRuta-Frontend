namespace NexoRuta.ApiClient.Features.Reparto.Contracts;

public sealed record ComandoFlotaRequest(Guid ComandoId, Guid? OcupacionId, long Version, string Motivo,
    Guid? EnvioId = null, long? VersionEnvio = null);
public sealed record ResultadoFlotaResponse(Guid ComandoId, Guid VehiculoId, Guid? RutaId, Guid? OcupacionId,
    long Version, string Accion, DateTimeOffset ConfirmadoEnUtc, Guid? EnvioId, long? VersionEnvio,
    IReadOnlyDictionary<Guid, long>? VersionesBultosAnteriores);
public sealed record OcupacionFlotaResponse(Guid Id, Guid RutaId, Guid RepartidorAccesoId, DateOnly Fecha,
    string EstadoRuta, long Version, bool Legada, DateTimeOffset? AdquiridaEnUtc, DateTimeOffset? RetornoEnUtc,
    int CargaPendiente, bool PuedeAnular);
public sealed record VehiculoFlotaResponse(Guid Id, string Matricula, bool Activo, long Version,
    DateTimeOffset? VerificacionInicialEnUtc, int CargaPendiente, IReadOnlyList<OcupacionFlotaResponse> Ocupaciones,
    DateTimeOffset? UltimaLiberacionEnUtc, IReadOnlyList<CargaInicialFlotaResponse> CargasIniciales);
public sealed record CargaInicialFlotaResponse(Guid EnvioId, long VersionEnvio, Guid RutaId, DateOnly Fecha, int BultosPendientes);
