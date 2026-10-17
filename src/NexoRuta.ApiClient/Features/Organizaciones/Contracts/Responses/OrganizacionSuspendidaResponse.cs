namespace NexoRuta.ApiClient.Features.Organizaciones.Contracts.Responses;

public sealed record OrganizacionSuspendidaResponse(
    Guid OrganizacionId,
    string TipoOrganizacion,
    string Nombre,
    string Estado,
    string Mensaje,
    bool EsPropietario);
