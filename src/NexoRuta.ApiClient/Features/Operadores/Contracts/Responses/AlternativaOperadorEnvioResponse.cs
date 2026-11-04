namespace NexoRuta.ApiClient.Features.Operadores.Contracts.Responses;

public sealed record AlternativaOperadorEnvioResponse(Guid OperadorId, string Nombre, IReadOnlyList<string> Modalidades);
