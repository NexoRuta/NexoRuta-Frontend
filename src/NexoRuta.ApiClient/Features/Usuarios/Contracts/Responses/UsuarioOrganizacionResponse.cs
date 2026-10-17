namespace NexoRuta.ApiClient.Features.Usuarios.Contracts.Responses;

public sealed record UsuarioOrganizacionResponse(
    Guid UsuarioId,
    Guid AccesoId,
    string Email,
    Guid? RolId,
    string? RolCodigo,
    string? RolNombre,
    bool Activo,
    long Version = 0,
    bool RolProtegido = false,
    bool EsPropietario = false);
