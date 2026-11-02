using System.ComponentModel.DataAnnotations;
using NexoRuta.ApiClient.Core.Validation;

namespace NexoRuta.ApiClient.Features.Reparto.Contracts;

public sealed class VehiculoResponse
{
    public Guid Id { get; set; }
    [Required, StringLength(20)] public string Matricula { get; set; } = "";
    [DosDecimales, Range(typeof(decimal), "0.01", "9999999999999999", ParseLimitsInInvariantCulture = true)]
    public decimal PesoMaximoGramos { get; set; }
    [DosDecimales, Range(typeof(decimal), "0.01", "9999999999999999999999", ParseLimitsInInvariantCulture = true)]
    public decimal VolumenMaximoCm3 { get; set; }
    public bool Activo { get; set; } = true;
    [Range(0, long.MaxValue)] public long Version { get; set; }
}
