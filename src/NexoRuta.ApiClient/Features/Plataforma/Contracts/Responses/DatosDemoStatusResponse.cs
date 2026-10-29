namespace NexoRuta.ApiClient.Features.Plataforma.Contracts.Responses;

public sealed record DatosDemoStatusResponse(
    bool Sembrada,
    DateTimeOffset? SembradoEnUtc,
    int Operadores,
    int Comercios,
    int CuentasOperador,
    int CuentasComercio,
    int Envios,
    int Rutas,
    int Operaciones);
