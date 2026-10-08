namespace NexoRuta.ApiClient.Contracts;

public sealed record ContextoDemoResponse(
    Guid UsuarioId,
    Guid OperadorId,
    Guid OperadorComercioId,
    string UsuarioEmail,
    string OperadorNombre,
    string ComercioNombre);
