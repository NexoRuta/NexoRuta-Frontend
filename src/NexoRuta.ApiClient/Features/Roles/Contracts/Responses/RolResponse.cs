namespace NexoRuta.ApiClient.Features.Roles.Contracts.Responses;

public sealed record RolResponse(Guid Id, string Codigo, string Nombre, IReadOnlyList<string> Permisos)
{
    public bool EsProtegidoSistema { get; init; }
    public bool EsPropio { get; init; }
}
