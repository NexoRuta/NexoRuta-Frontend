using System.Net;

namespace NexoRuta.ApiClient.Features.Envios.Contracts.Responses;

public sealed record ImportacionEnviosResponse(
    string Estado,
    ResumenImportacionEnviosResponse Resumen,
    IReadOnlyList<FilaImportacionEnviosResponse> Filas);

public sealed record ResumenImportacionEnviosResponse(
    int Creados,
    int Existentes,
    int DuplicadosEnArchivo,
    int Conflictos,
    int Invalidos,
    int NoProcesados);

public sealed record FilaImportacionEnviosResponse(
    int Registro,
    string? ReferenciaExterna,
    string Estado,
    Guid? EnvioId,
    int? MismoQueRegistro,
    IReadOnlyList<ErrorFilaImportacionEnviosResponse> Errores);

public sealed record ErrorFilaImportacionEnviosResponse(string Codigo, string? Campo, string Mensaje);

public sealed record ErrorImportacionEnviosResponse(string Tipo, string Codigo, string Mensaje);

public sealed record ImportacionEnviosApiResult(HttpStatusCode StatusCode, ImportacionEnviosResponse Response);
