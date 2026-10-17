namespace NexoRuta.ApiClient.Features.Plataforma.Contracts.Responses;

public sealed record SolicitudBajaAdminResponse(
    Guid Id,
    string TipoOrganizacion,
    Guid OrganizacionId,
    string NombreOrganizacion,
    string Estado,
    string Motivo,
    Guid SolicitadaPorUsuarioId,
    DateTimeOffset SolicitadaEnUtc,
    Guid? ResueltaPorUsuarioId,
    DateTimeOffset? ResueltaEnUtc,
    string? MotivoResolucion,
    long Version);

public sealed record PaginaSolicitudesBajaResponse(
    IReadOnlyList<SolicitudBajaAdminResponse> Items,
    int Pagina,
    int TamanoPagina,
    int Total);
