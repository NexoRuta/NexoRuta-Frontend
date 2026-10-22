namespace NexoRuta.ApiClient.Features.Seguimiento.Contracts.Responses;

public sealed record SeguimientoEstadoResponse(
    string Estado,
    NexoRuta.ApiClient.Features.IdentidadVisual.Contracts.Responses.IdentidadVisualPublicaResponse? Identidad = null);
