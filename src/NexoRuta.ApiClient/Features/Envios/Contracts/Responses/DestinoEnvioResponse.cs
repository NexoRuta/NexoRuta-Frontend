namespace NexoRuta.ApiClient.Features.Envios.Contracts.Responses;

public sealed record DestinoEnvioResponse(Guid EnvioId, string Direccion, long Version, string Estado,
    double? Latitud, double? Longitud, DateTimeOffset? ConfirmadaEnUtc);
