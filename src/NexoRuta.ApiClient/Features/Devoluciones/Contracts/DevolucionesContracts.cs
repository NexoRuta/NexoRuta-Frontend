namespace NexoRuta.ApiClient.Features.Devoluciones.Contracts;

public sealed record TransferirDevolucionRequest(Guid ComandoId, long VersionEnvio,
    IReadOnlyList<BultoDevolucionRequest> Bultos, bool ConfirmarTransferenciaFisica);
public sealed record BultoDevolucionRequest(Guid BultoId, long VersionBulto);
public sealed record TransferenciaDevolucionResponse(Guid TransferenciaId, int Bultos, DateTimeOffset RegistradaEnUtc);

public sealed record AcusarDevolucionRequest(Guid ComandoId, long VersionTransferencia, bool Conforme,
    IReadOnlyList<BultoAcuseDevolucionRequest> Bultos);
public sealed record BultoAcuseDevolucionRequest(Guid BultoId, long VersionBulto, string? Observacion);
public sealed record AcuseDevolucionResponse(Guid AcuseId, int Bultos, string Resultado, DateTimeOffset RegistradoEnUtc);

public sealed record DevolucionResumenResponse(Guid EnvioId, Guid OperadorId, string OperadorNombre,
    string Referencia, string EstadoEnvio, DateTimeOffset ActividadEnUtc, int TotalBultos, int EnRetorno,
    int EnDeposito, int PendientesAcuse, int RecibidosConObservaciones, string Situacion);
public sealed record PaginaDevolucionesResponse(IReadOnlyList<DevolucionResumenResponse> Items, string? CursorSiguiente);

public sealed record BultoDevolucionResponse(Guid Id, string Codigo, string Resultado, string Custodia,
    long Version, Guid? TransferenciaId, string Situacion, bool PuedeAcusar, string? MotivoBloqueo,
    string? ResultadoAcuse, string? Observacion, DateTimeOffset? TransferidoEnUtc, DateTimeOffset? AcusadoEnUtc);
public sealed record DevolucionDetalleResponse(Guid EnvioId, Guid OperadorId, string OperadorNombre,
    string Referencia, string EstadoEnvio, long VersionEnvio, bool EsRetiro, int TotalBultos,
    int PendientesAcuse, IReadOnlyList<BultoDevolucionResponse> Bultos);
public sealed record HechoDevolucionResponse(Guid Id, string Tipo, DateTimeOffset RegistradoEnUtc,
    DateTimeOffset? OcurridoEnUtc, string Actor, int? Bultos, string? Resultado, string? Observaciones);
