namespace NexoRuta.ApiClient.Features.Reparto.Contracts;

public sealed class ConfiguracionRepartoResponse
{
    public long Version { get; set; }
    public string ZonaHoraria { get; set; } = "";
    public int MaximoParadas { get; set; }
    public bool RequiereFotoIntento { get; set; }
    public bool BloquearDiscrepanciasCarga { get; set; }
}
