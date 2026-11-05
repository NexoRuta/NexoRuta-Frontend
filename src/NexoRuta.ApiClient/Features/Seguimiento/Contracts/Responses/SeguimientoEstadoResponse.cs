namespace NexoRuta.ApiClient.Features.Seguimiento.Contracts.Responses;

public sealed record SeguimientoEstadoResponse(
    string Estado,
    NexoRuta.ApiClient.Features.IdentidadVisual.Contracts.Responses.IdentidadVisualPublicaResponse? Identidad = null) { public int TotalBultos { get; init; } public int BultosEntregados { get; init; } public int BultosDevueltos { get; init; } public bool EsRetiro { get; init; } }
