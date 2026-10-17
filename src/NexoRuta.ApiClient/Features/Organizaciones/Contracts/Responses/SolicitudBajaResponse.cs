namespace NexoRuta.ApiClient.Features.Organizaciones.Contracts.Responses;

public sealed record SolicitudBajaResponse(
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
