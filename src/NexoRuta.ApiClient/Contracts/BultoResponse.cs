namespace NexoRuta.ApiClient.Contracts;

public sealed record BultoResponse(
    string Codigo,
    decimal PesoGramos,
    decimal LargoCentimetros,
    decimal AnchoCentimetros,
    decimal AltoCentimetros);
