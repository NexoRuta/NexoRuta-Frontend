using System.ComponentModel.DataAnnotations;
using NexoRuta.ApiClient.Core.Validation;

namespace NexoRuta.ApiClient.Features.Envios.Contracts.Requests;

public sealed class CrearEnvioRequest : IValidatableObject
{
    [Required(ErrorMessage = "Seleccioná un operador para el envío.")]
    public Guid? OperadorId { get; set; }

    [Required, StringLength(160)]
    public string DestinatarioNombre { get; set; } = "";

    [Required, EmailAddress(ErrorMessage = "Ingresá un correo electrónico válido."), StringLength(254)]
    public string DestinatarioEmail { get; set; } = "";

    [Required, StringLength(240)]
    public string Direccion { get; set; } = "";

    [Required, MinLength(1)]
    public List<BultoDeclaradoRequest> Bultos { get; set; } = [];
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Bultos is null) yield break;
        foreach (var bulto in Bultos)
        {
            if (bulto is null) { yield return new ValidationResult("Completá los datos del bulto."); continue; }
            var errors = new List<ValidationResult>();
            Validator.TryValidateObject(bulto, new ValidationContext(bulto), errors, true);
            foreach (var error in errors) yield return error;
        }
        if (Bultos.Select(b => b?.Codigo?.Trim()).Distinct(StringComparer.Ordinal).Count() != Bultos.Count)
            yield return new ValidationResult("Los códigos de bulto no pueden repetirse dentro del envío.");
    }
}

public sealed class BultoDeclaradoRequest
{
    [Required, StringLength(80)]
    public string Codigo { get; set; } = "";

    [Required, Range(typeof(decimal), "0.01", "999999999", ParseLimitsInInvariantCulture = true)]
    [DosDecimales] public decimal PesoGramos { get; set; }

    [Required, Range(typeof(decimal), "0.01", "999999999", ParseLimitsInInvariantCulture = true)]
    [DosDecimales] public decimal LargoCentimetros { get; set; }

    [Required, Range(typeof(decimal), "0.01", "999999999", ParseLimitsInInvariantCulture = true)]
    [DosDecimales] public decimal AnchoCentimetros { get; set; }

    [Required, Range(typeof(decimal), "0.01", "999999999", ParseLimitsInInvariantCulture = true)]
    [DosDecimales] public decimal AltoCentimetros { get; set; }
}
