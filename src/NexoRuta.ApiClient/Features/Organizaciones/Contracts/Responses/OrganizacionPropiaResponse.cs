namespace NexoRuta.ApiClient.Features.Organizaciones.Contracts.Responses;

public sealed record OrganizacionPropiaResponse(
    Guid Id,
    string Nombre,
    long Version,
    string Estado,
    bool EsPropietario,
    bool TieneDuenoRegistrado);
