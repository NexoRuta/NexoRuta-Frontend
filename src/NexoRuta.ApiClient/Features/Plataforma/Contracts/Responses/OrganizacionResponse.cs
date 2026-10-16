namespace NexoRuta.ApiClient.Features.Plataforma.Contracts.Responses;

public sealed record OrganizacionResponse(Guid Id, string Nombre);
public sealed record PaginaOrganizacionesResponse(IReadOnlyList<OrganizacionResponse> Items, int Pagina, int TamanoPagina, int Total);
public sealed record AltaOrganizacionResponse(OrganizacionResponse Organizacion, Guid UsuarioId, Guid AccesoId, Guid RolId);
