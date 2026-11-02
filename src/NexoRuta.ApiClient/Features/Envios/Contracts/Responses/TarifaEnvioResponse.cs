namespace NexoRuta.ApiClient.Features.Envios.Contracts.Responses;
public sealed record TarifaEnvioResponse(Guid VersionTarifarioId, Guid ZonaId, string Modalidad, string Moneda, decimal TotalFinalUyu, decimal ImporteBaseUyu, decimal ComponentePesoUyu, decimal ComponenteVolumenUyu, decimal TotalRecargosUyu, decimal TotalBonificacionesUyu);
