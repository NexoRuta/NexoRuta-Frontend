namespace NexoRuta.ApiClient.Features.Reparto.Contracts;
public sealed record CotejoBultoRequest(Guid ComandoId, long VersionEnvio, long VersionBulto, bool Conforme,
    decimal? PesoObservado, decimal? LargoObservado, decimal? AnchoObservado, decimal? AltoObservado, string? Observaciones);
public sealed record MedidasRecepcionResponse(decimal PesoGramos, decimal LargoCentimetros, decimal AnchoCentimetros, decimal AltoCentimetros);
public sealed record HechoRecepcionResponse(Guid ComandoId, Guid BultoId, Guid UsuarioId, Guid AccesoId,
    DateTimeOffset RegistradaEnUtc, MedidasRecepcionResponse Declarado, CotejoBultoRequest Cotejo);
public sealed record BultoDepositoResponse(Guid Id, string Codigo, long Version, string Custodia,
    MedidasRecepcionResponse Declarado, string CotejoActual);
public sealed record ConsultaRecepcionResponse(Guid EnvioId, string EstadoEnvio, long VersionEnvio, string Destinatario,
    Guid BultoSeleccionadoId, IReadOnlyList<BultoDepositoResponse> Bultos, IReadOnlyList<HechoRecepcionResponse> Historial);
public sealed record ResultadoRecepcionResponse(bool Repetida, ConsultaRecepcionResponse Recepcion);
