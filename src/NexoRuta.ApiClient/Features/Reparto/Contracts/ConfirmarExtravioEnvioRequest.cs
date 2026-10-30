namespace NexoRuta.ApiClient.Features.Reparto.Contracts;

public sealed record ConfirmarExtravioEnvioRequest(long VersionEnvio, Guid ComandoId, string Motivo);
