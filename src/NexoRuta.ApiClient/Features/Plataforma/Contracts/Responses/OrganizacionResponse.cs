namespace NexoRuta.ApiClient.Features.Plataforma.Contracts.Responses;

public sealed record OrganizacionResponse(Guid Id, string Nombre, string Estado = "Activa", long Version = 0);
public sealed record PaginaOrganizacionesResponse(IReadOnlyList<OrganizacionResponse> Items, int Pagina, int TamanoPagina, int Total);
public sealed record AltaOrganizacionResponse(OrganizacionResponse Organizacion, Guid UsuarioId, Guid AccesoId, Guid RolId);
