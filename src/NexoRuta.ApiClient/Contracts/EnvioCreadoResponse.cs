namespace NexoRuta.ApiClient.Contracts;

public sealed record EnvioCreadoResponse(
    Guid Id,
    Guid OperadorId,
    Guid OperadorComercioId,
    Guid CreadoPorUsuarioId,
    string UsuarioEmail,
    string OperadorNombre,
    string ComercioNombre,
    string CodigoBulto,
    decimal PesoGramos,
    decimal LargoCentimetros,
    decimal AnchoCentimetros,
    decimal AltoCentimetros);
