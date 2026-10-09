using System.Net;

namespace NexoRuta.ApiClient.Excepciones;

public sealed class NexoRutaApiException(string message, HttpStatusCode statusCode)
    : HttpRequestException(message, null, statusCode);
