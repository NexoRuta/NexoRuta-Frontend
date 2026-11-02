namespace NexoRuta.ApiClient.Features.Reparto.Contracts;

public sealed record IniciarDevolucionEnvioRequest(long VersionEnvio, Guid ComandoId, string Motivo);
