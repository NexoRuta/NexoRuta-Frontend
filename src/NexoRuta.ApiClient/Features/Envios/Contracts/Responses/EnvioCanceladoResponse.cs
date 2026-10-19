namespace NexoRuta.ApiClient.Features.Envios.Contracts.Responses;

public sealed record EnvioCanceladoResponse(Guid CancelacionId, Guid EnvioId, long Version, string Estado);
