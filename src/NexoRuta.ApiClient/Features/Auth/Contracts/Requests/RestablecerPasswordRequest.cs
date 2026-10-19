using System.ComponentModel.DataAnnotations;

namespace NexoRuta.ApiClient.Features.Auth.Contracts.Requests;

public sealed record RestablecerPasswordRequest(
    [property: Required] Guid UsuarioId,
    [property: Required, StringLength(4096, MinimumLength = 1)] string Token,
    [property: Required, StringLength(1024, MinimumLength = 1)] string NuevaPassword);
