namespace NexoRuta.ApiClient.Features.Envios.Contracts.Responses;

public sealed record BultoResponse(
    string Codigo,
    decimal PesoGramos,
    decimal LargoCentimetros,
    decimal AnchoCentimetros,
    decimal AltoCentimetros);
