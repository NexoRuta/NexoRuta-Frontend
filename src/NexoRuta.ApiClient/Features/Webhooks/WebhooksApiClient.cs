using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Http;
namespace NexoRuta.ApiClient.Features.Webhooks;
public sealed record WebhookSubscription(Guid Id,Guid OperadorId,Guid ComercioId,string Url,string[] Eventos,int Contrato,bool Habilitada,bool Revocada,long Revision,Guid ClaveId,DateTimeOffset ActualizadaEnUtc);
public sealed record SaveWebhook(Guid OperadorId,string Url,string[] Eventos,int Contrato,bool Habilitada,long Revision,string? Secreto);
public sealed record WebhookDelivery(Guid Id,Guid EventoId,Guid SuscripcionId,long Revision,Guid OperadorId,Guid ComercioId,Guid EnvioId,string Tipo,int Contrato,string Url,string Payload,Guid ClaveId,string Estado,int Intentos,int IntentosCiclo,int Reenvios,DateTimeOffset ProximoIntentoUtc,DateTimeOffset CreadaEnUtc,DateTimeOffset? FinalizadaEnUtc,int? CodigoHttp,string? Diagnostico);
public sealed record WebhookAttempt(Guid EntregaId,int Numero,DateTimeOffset InicioUtc,DateTimeOffset? FinUtc,int? CodigoHttp,string? Diagnostico);
public sealed record WebhookReplay(Guid EntregaId,int Numero,Guid ActorId,DateTimeOffset FechaUtc,string EstadoAnterior);
public sealed record WebhookDetail(WebhookDelivery Entrega,WebhookAttempt[] Intentos,WebhookReplay[] Reenvios,int Pagina);
public sealed record WebhookHistory(int Total,int Pagina,int Tamano,WebhookDelivery[] Entregas);
public sealed class WebhooksApiClient(HttpClient http)
{
    public Task<WebhookSubscription[]> Subscriptions(Guid op,string token,CancellationToken ct=default)=>Send<WebhookSubscription[]>(HttpMethod.Get,$"api/webhooks/suscripciones?operadorId={op}",null,token,ct);
    public Task<WebhookSubscription> Save(Guid id,SaveWebhook command,string token,CancellationToken ct=default)=>Send<WebhookSubscription>(HttpMethod.Put,$"api/webhooks/suscripciones/{id}",command,token,ct);
    public Task<object> Revoke(Guid id,long revision,string token,CancellationToken ct=default)=>Send<object>(HttpMethod.Post,$"api/webhooks/suscripciones/{id}/revocaciones",revision,token,ct);
    public Task<WebhookHistory> History(Guid op,string state,string type,int page,string token,CancellationToken ct=default)=>Send<WebhookHistory>(HttpMethod.Get,$"api/webhooks/entregas?operadorId={op}&pagina={page}&tamano=25{(state.Length>0?"&estado="+Uri.EscapeDataString(state):"")}{(type.Length>0?"&tipo="+Uri.EscapeDataString(type):"")}",null,token,ct);
    public Task<WebhookDetail> Detail(Guid id,int page,string token,CancellationToken ct=default)=>Send<WebhookDetail>(HttpMethod.Get,$"api/webhooks/entregas/{id}?pagina={page}",null,token,ct);
    public Task<object> Replay(Guid id,string token,CancellationToken ct=default)=>Send<object>(HttpMethod.Post,$"api/webhooks/entregas/{id}/reenvios",new{},token,ct);
    private async Task<T> Send<T>(HttpMethod method,string url,object? body,string token,CancellationToken ct) where T:class
    {
        using var request=ApiHttp.WithBearerToken(new HttpRequestMessage(method,url),token);
        if(body is not null) request.Content=JsonContent.Create(body);
        return await ApiHttp.SendAsync<T>(http,request,ct);
    }
}
