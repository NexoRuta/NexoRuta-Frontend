using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Http;
namespace NexoRuta.ApiClient.Features.Gestion;
public sealed class GestionApiClient(HttpClient http)
{
    public Task<ReporteGestion> ReporteAsync(FiltroGestion f,string token,CancellationToken ct=default)
        => Send<ReporteGestion>(HttpMethod.Get,Url(f,"reportes"),null,token,ct);
    public Task<DetalleGestion> DetalleAsync(Guid op,Guid envio,string token,CancellationToken ct=default)
        => Send<DetalleGestion>(HttpMethod.Get,$"api/gestion/operadores/{op}/reportes/operaciones/{envio}",null,token,ct);
    public Task<CuentaGestion> CuentaAsync(FiltroGestion f,string token,CancellationToken ct=default)
        => Send<CuentaGestion>(HttpMethod.Get,Url(f,"cuenta"),null,token,ct);
    public Task<LiquidacionDetalle> DocumentoAsync(Guid op,Guid id,string token,CancellationToken ct=default)
        => Send<LiquidacionDetalle>(HttpMethod.Get,$"api/gestion/operadores/{op}/liquidaciones/{id}",null,token,ct);
    public Task<LiquidacionDetalle> PrepararAsync(Guid op,PrepararLiquidacion c,string token,CancellationToken ct=default)
        => Send<LiquidacionDetalle>(HttpMethod.Post,$"api/gestion/operadores/{op}/liquidaciones",c,token,ct);
    public Task<LiquidacionDetalle> TransicionAsync(Guid op,Guid id,bool confirmar,string token,CancellationToken ct=default)
        => Send<LiquidacionDetalle>(HttpMethod.Post,$"api/gestion/operadores/{op}/liquidaciones/{id}/{(confirmar ? "confirmar" : "descartar")}",null,token,ct);
    public Task<CorreccionRegistrada> CorregirAsync(Guid op,CorregirCuenta c,string token,CancellationToken ct=default)
        => Send<CorreccionRegistrada>(HttpMethod.Post,$"api/gestion/operadores/{op}/correcciones",c,token,ct);
    private async Task<T> Send<T>(HttpMethod method,string url,object? body,string token,CancellationToken ct) where T : class
    {
        using var request=ApiHttp.WithBearerToken(new HttpRequestMessage(method,url),token);
        if(body is not null) request.Content=JsonContent.Create(body);
        return await ApiHttp.SendAsync<T>(http,request,ct);
    }
    private static string Url(FiltroGestion f,string resource)
        => $"api/gestion/operadores/{f.OperadorId}/{resource}?desde={Uri.EscapeDataString(f.Desde.ToString("O"))}&hasta={Uri.EscapeDataString(f.Hasta.ToString("O"))}&comercioId={f.ComercioId}&zonaId={f.ZonaId}&repartidorId={f.RepartidorId}&pagina={f.Pagina}&tamano={f.Tamano}";
}
public static class PeriodoGestion
{
    public static readonly TimeZoneInfo Zona=TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo");
    public static DateOnly Hoy=>DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,Zona).DateTime);
    public static DateTimeOffset Inicio(DateOnly dia)=>new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(dia.ToDateTime(TimeOnly.MinValue),Zona));
    public static string Fecha(DateTimeOffset? utc)=>utc is null ? "Sin registro" : TimeZoneInfo.ConvertTime(utc.Value,Zona).ToString("dd/MM/yyyy HH:mm:ss");
}
