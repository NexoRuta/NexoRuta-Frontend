namespace NexoRuta.ApiClient.Features.Envios.Contracts.Responses;

public sealed record PropuestasDestinoResponse(Guid EnvioId, string Direccion, long Version,
    string Atribucion, IReadOnlyList<PropuestaDestinoResponse> Propuestas);
