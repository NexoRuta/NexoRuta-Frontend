namespace NexoRuta.ApiClient.Features.Reparto.Contracts;

public sealed record ReglasEnvioResponse(Guid VersionId, int NumeroVersion, int MaximoIntentos,
    int IntervaloEntreIntentosMinutos, string PoliticaDevolucionAlAgotarIntentos,
    bool RequiereFirma, bool RequiereFoto, bool RequiereNombre, bool RequiereDocumento,
    IReadOnlyList<MotivoNoEntregaResponse> Motivos);
public sealed record MotivoNoEntregaResponse(string Codigo, string Descripcion);
