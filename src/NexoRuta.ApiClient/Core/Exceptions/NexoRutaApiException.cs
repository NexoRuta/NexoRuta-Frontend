using System.Net;

namespace NexoRuta.ApiClient.Core.Exceptions;

public sealed class NexoRutaApiException(string message, HttpStatusCode statusCode)
    : HttpRequestException(message, null, statusCode);
