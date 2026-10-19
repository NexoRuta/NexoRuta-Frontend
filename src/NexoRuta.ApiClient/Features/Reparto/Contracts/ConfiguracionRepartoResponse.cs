namespace NexoRuta.ApiClient.Features.Reparto.Contracts;

public sealed class ConfiguracionRepartoResponse
{
    public long Version { get; set; }
    public string ZonaHoraria { get; set; } = "";
    public int MaximoParadas { get; set; }
    public int MaximoIntentos { get; set; }
    public bool RequiereFirma { get; set; }
    public bool RequiereFoto { get; set; }
    public bool RequiereNombre { get; set; }
    public bool RequiereDocumento { get; set; }
    public bool RequiereFotoIntento { get; set; }
    public bool BloquearDiscrepanciasCarga { get; set; }
    public string[] Motivos { get; set; } = [];
}
