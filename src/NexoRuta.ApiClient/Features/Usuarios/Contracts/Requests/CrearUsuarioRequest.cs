namespace NexoRuta.ApiClient.Features.Usuarios.Contracts.Requests;

public sealed record CrearUsuarioRequest(string Email, string Password, Guid RolId);
