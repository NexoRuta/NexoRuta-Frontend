using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using NexoRuta.Tracking.CommerceDemo;
namespace NexoRuta.ApiClient.Tests;
public sealed class CommerceReceiverTests
{
    [Fact] public void SignatureRejectsTamperingExpiredFutureInvalidAndAcceptsRotation()
    {
        var key=RandomNumberGenerator.GetBytes(32);var secret="whsec_"+Convert.ToBase64String(key);var id=Guid.NewGuid().ToString();var now=DateTimeOffset.UtcNow;
        var stamp=now.ToUnixTimeSeconds().ToString();var body=Encoding.UTF8.GetBytes("{\"estado\":\"Entregado\"}");
        var signature="v1,"+Convert.ToBase64String(HMACSHA256.HashData(key,Encoding.UTF8.GetBytes(id+"."+stamp+".").Concat(body).ToArray()));
        Assert.True(ReceiverEndpoint.Verify(["whsec_"+Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),secret],id,stamp,signature,body,now));
        Assert.False(ReceiverEndpoint.Verify([secret],id,stamp,signature,body.Concat(new byte[]{32}).ToArray(),now));
        Assert.False(ReceiverEndpoint.Verify([secret],id,stamp,signature,body,now.AddMinutes(6)));
        Assert.False(ReceiverEndpoint.Verify([secret],id,stamp,signature,body,now.AddMinutes(-6)));
        Assert.False(ReceiverEndpoint.Verify([secret],id,stamp,"v1,invalid",body,now));
    }
    [Fact] public void RestartPreservesOutageDedupAndLostResponseEffect()
    {
        var dir=Path.Combine(Path.GetTempPath(),"nr-receiver-"+Guid.NewGuid().ToString("N"));
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"CommerceDemo:DatabasePath",Path.Combine(dir,"receiver.db")}}).Build();
        try
        {
            var store=new ReceiverStore(config);store.Mode("FallaSimulada");
            var id=Guid.NewGuid().ToString();var item=new ReceiverEvent(id,"OperacionActualizada",2,Guid.NewGuid().ToString(),"Entregado","1 de 1",DateTimeOffset.UtcNow.ToString("O"),DateTimeOffset.UtcNow,"{}");
            Assert.Equal(503,new ReceiverStore(config).Receive(id,true,item).Status);
            Assert.Equal(0,store.Inbox(1).Processed);store.Mode("RespuestaPerdida");
            Assert.True(store.Receive(id,true,item).Abort);
            var restarted=new ReceiverStore(config);Assert.Equal(1,restarted.Inbox(1).Processed);
            Assert.Equal(200,restarted.Receive(id,true,item).Status);
            Assert.Equal(1,restarted.Inbox(1).Processed);Assert.Contains("Duplicado",restarted.Inbox(1).RecentAttempts[0].Result);
            Assert.Equal(401,restarted.Receive(Guid.NewGuid().ToString(),false,item).Status);
            Assert.Equal(409,restarted.Receive(id,true,item with{Payload="{\"alterado\":true}"}).Status);
            Assert.Equal(1,restarted.Inbox(1).Processed);Assert.Equal(5,restarted.Inbox(1).Attempts);
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
    }
}
