using System.ComponentModel.DataAnnotations;

namespace NexoRuta.ApiClient.Features.Auth.Contracts.Requests;

public sealed record SolicitarRecuperacionPasswordRequest(
    [property: Required, EmailAddress, StringLength(254)] string Email);
