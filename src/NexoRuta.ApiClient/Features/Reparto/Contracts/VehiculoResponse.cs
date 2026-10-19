namespace NexoRuta.ApiClient.Features.Reparto.Contracts;

public sealed class VehiculoResponse
{
    public Guid Id { get; set; }
    public string Matricula { get; set; } = "";
    public decimal PesoMaximoGramos { get; set; }
    public decimal VolumenMaximoCm3 { get; set; }
    public bool Activo { get; set; } = true;
    public long Version { get; set; }
}
