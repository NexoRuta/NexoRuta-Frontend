namespace NexoRuta.ApiClient.Features.Auth.Contracts.Responses;

public sealed record LoginResponse(string AccessToken, string TokenType, DateTime ExpiresAtUtc);
