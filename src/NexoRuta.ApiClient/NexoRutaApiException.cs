using System.Net;

namespace NexoRuta.ApiClient;

public sealed class NexoRutaApiException(string message, HttpStatusCode statusCode)
    : HttpRequestException(message, null, statusCode);
