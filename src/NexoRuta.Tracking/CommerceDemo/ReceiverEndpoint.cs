using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace NexoRuta.Tracking.CommerceDemo;

public static class ReceiverEndpoint
{
    public static bool Verify(string[] secrets,string id,string stamp,string signature,byte[] body,DateTimeOffset now)
    {
        if(!Guid.TryParse(id,out _) || !long.TryParse(stamp,NumberStyles.None,CultureInfo.InvariantCulture,out var seconds) ||
            Math.Abs((double)now.ToUnixTimeSeconds()-seconds)>300 || signature.Length>1024) return false;
        foreach(var secret in secrets)
        {
            if(!secret.StartsWith("whsec_",StringComparison.Ordinal)) continue;
            byte[] key;
            try {key=Convert.FromBase64String(secret[6..]);} catch(FormatException) {continue;}
            if(key.Length!=32) {CryptographicOperations.ZeroMemory(key);continue;}
            try
            {
                using var mac=IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256,key);
                mac.AppendData(Encoding.UTF8.GetBytes(id+"."+stamp+"."));mac.AppendData(body);
                var expected=mac.GetHashAndReset();
                foreach(var part in signature.Split(' ',StringSplitOptions.RemoveEmptyEntries))
                {
                    if(!part.StartsWith("v1,",StringComparison.Ordinal)) continue;
                    try {if(CryptographicOperations.FixedTimeEquals(expected,Convert.FromBase64String(part[3..]))) return true;}
                    catch(FormatException) { }
                }
            }
            finally {CryptographicOperations.ZeroMemory(key);}
        }
        return false;
    }
    public static async Task Receive(HttpContext context,ReceiverStore store,IConfiguration config)
    {
        context.Response.Headers.CacheControl="no-store";
        if(context.Request.ContentLength>65536) {context.Response.StatusCode=413;return;}
        if(context.Request.ContentType?.Split(';')[0].Trim()!="application/json") {context.Response.StatusCode=415;return;}
        using var buffer=new MemoryStream();var chunk=new byte[4096];int read;
        while((read=await context.Request.Body.ReadAsync(chunk,context.RequestAborted))>0)
        {
            if(buffer.Length+read>65536) {context.Response.StatusCode=413;return;}
            await buffer.WriteAsync(chunk.AsMemory(0,read),context.RequestAborted);
        }
        var body=buffer.ToArray();var id=context.Request.Headers["webhook-id"].ToString();
        var valid=Verify(config.GetSection("CommerceDemo:SigningSecrets").Get<string[]>()??[],id,context.Request.Headers["webhook-timestamp"].ToString(),context.Request.Headers["webhook-signature"].ToString(),body,DateTimeOffset.UtcNow);
        ReceiverEvent? item=null;
        if(valid)
        {
            try
            {
                using var json=JsonDocument.Parse(body,new JsonDocumentOptions{MaxDepth=16});var root=json.RootElement;
                var version=root.TryGetProperty("schemaVersion",out var v)?v.GetInt32():1;
                var shipment=root.GetProperty("envioId").GetGuid();var state=root.GetProperty("estado").GetString()!;
                var type=version==2?root.GetProperty("tipo").GetString()!:(state=="Admitido"?"EnvioCreado":"OperacionActualizada");
                var headerVersion=context.Request.Headers["NexoRuta-Contract-Version"].ToString();
                var headerType=context.Request.Headers["NexoRuta-Event-Type"].ToString();
                if(version is not (1 or 2) || headerVersion!=version.ToString(CultureInfo.InvariantCulture) || type!=headerType || shipment==Guid.Empty ||
                    version==2 && (root.GetProperty("eventId").GetGuid().ToString("D")!=id || !root.TryGetProperty("ocurridaEnUtc",out _))) throw new JsonException();
                var progress=version==2 && root.TryGetProperty("progreso",out var p)?$"{p.GetProperty("entregados").GetInt32()} de {p.GetProperty("total").GetInt32()} bultos entregados":"Contrato anterior: sin detalle de bultos";
                if(version==2 && root.TryGetProperty("esRetiro",out var pickup) && pickup.GetBoolean() && root.GetProperty("progreso").TryGetProperty("retirados",out var picked))
                    progress=$"{picked.GetInt32()} de {root.GetProperty("progreso").GetProperty("total").GetInt32()} bultos retirados · {root.GetProperty("progreso").GetProperty("entregados").GetInt32()} entregados al comercio";
                var occurred=version==2?root.GetProperty("ocurridaEnUtc").GetString():null;
                item=new(id,type,version,shipment.ToString(),state,progress,occurred,DateTimeOffset.UtcNow,Encoding.UTF8.GetString(body));
            }
            catch(Exception ex) when(ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { }
        }
        var result=store.Receive(Guid.TryParse(id,out _)?id:null,valid,item);
        if(result.Abort) {context.Abort();return;}
        context.Response.StatusCode=result.Status;
        if(result.Status==503) context.Response.Headers.RetryAfter="2";
        await context.Response.WriteAsJsonAsync(new {accepted=result.Status==200},context.RequestAborted);
    }
}
