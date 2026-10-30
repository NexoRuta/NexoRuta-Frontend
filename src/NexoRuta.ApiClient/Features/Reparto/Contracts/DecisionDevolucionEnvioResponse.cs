namespace NexoRuta.ApiClient.Features.Reparto.Contracts;

public sealed record DecisionDevolucionEnvioResponse(Guid EnvioId, Guid DecisionId, Guid ComandoId,
    string EstadoDecision, long VersionDecision, string Motivo, DateTimeOffset DecididaEnUtc,
    string EstadoActual, long VersionActual, bool Repetida);
