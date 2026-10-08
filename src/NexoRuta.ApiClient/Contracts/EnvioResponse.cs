namespace NexoRuta.ApiClient.Contracts;

public sealed record EnvioResponse(
    Guid Id,
    Guid OperadorId,
    Guid OperadorComercioId,
    Guid CreadoPorUsuarioId,
    string UsuarioEmail,
    string OperadorNombre,
    string ComercioNombre,
    string DestinatarioNombre,
    string Direccion,
    string Estado,
    IReadOnlyList<BultoResponse> Bultos);
