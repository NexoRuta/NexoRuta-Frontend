namespace NexoRuta.ApiClient.Features.Reparto.Contracts;

public sealed record ConfirmacionExtravioEnvioResponse(Guid EnvioId, Guid ConfirmacionId, Guid ComandoId,
    string EstadoConfirmado, long VersionAnterior, long VersionNueva, string Motivo, DateTimeOffset ConfirmadaEnUtc,
    string EstadoActual, long VersionActual, bool Repetida);
