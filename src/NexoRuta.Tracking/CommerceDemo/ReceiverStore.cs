using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
namespace NexoRuta.Tracking.CommerceDemo;

public sealed record ReceiverAttempt(long Id, string? EventId, string Result, bool SignatureValid, DateTimeOffset ReceivedAt);
public sealed record ReceiverEvent(string Id, string Type, int Version, string Shipment, string State, string Progress, string? OccurredAt, DateTimeOffset ReceivedAt, string Payload);
public sealed record ReceiverInbox(string Mode, long Processed, long Attempts, ReceiverEvent[] Events, ReceiverAttempt[] RecentAttempts);

/// <summary>Independent receiver database. No NexoRuta database connection or internal API client.</summary>
public sealed class ReceiverStore
{
    private readonly string connection;
    public ReceiverStore(IConfiguration config)
    {
        var path=Path.GetFullPath(config["CommerceDemo:DatabasePath"] ?? throw new InvalidOperationException("CommerceDemo:DatabasePath is required."));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        connection=new SqliteConnectionStringBuilder {DataSource=path,Mode=SqliteOpenMode.ReadWriteCreate,DefaultTimeout=10}.ToString();
        using var db=Open(); using var cmd=db.CreateCommand();
        cmd.CommandText="""
        PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA max_page_count=65536;
        CREATE TABLE IF NOT EXISTS config(id INTEGER PRIMARY KEY CHECK(id=1),mode TEXT NOT NULL);
        INSERT OR IGNORE INTO config VALUES(1,'Disponible');
        CREATE TABLE IF NOT EXISTS events(id TEXT PRIMARY KEY,type TEXT NOT NULL,version INTEGER NOT NULL,shipment TEXT NOT NULL,state TEXT NOT NULL,progress TEXT NOT NULL,occurred TEXT,received TEXT NOT NULL,payload TEXT NOT NULL,hash TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS attempts(id INTEGER PRIMARY KEY AUTOINCREMENT,event_id TEXT,result TEXT NOT NULL,valid INTEGER NOT NULL,received TEXT NOT NULL);
        """;
        cmd.ExecuteNonQuery();
        if(!OperatingSystem.IsWindows()) File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);
    }
    private SqliteConnection Open() {var db=new SqliteConnection(connection);db.Open();return db;}
    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction? tx, string sql, params (string,object?)[] args)
    {
        var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;
        foreach(var (k,v) in args) cmd.Parameters.AddWithValue(k,v??DBNull.Value);
        return cmd;
    }
    public void Mode(string mode)
    {
        if(mode is not ("Disponible" or "FallaSimulada" or "RespuestaPerdida")) throw new ArgumentException("Modo inválido.");
        using var db=Open();using var cmd=Command(db,null,"UPDATE config SET mode=$mode WHERE id=1",("$mode",mode));cmd.ExecuteNonQuery();
    }
    public (int Status,bool Abort) Receive(string? id, bool valid, ReceiverEvent? item)
    {
        using var db=Open();using var tx=db.BeginTransaction();
        using var modeCommand=Command(db,tx,"SELECT mode FROM config WHERE id=1");
        var mode=(string)modeCommand.ExecuteScalar()!;
        int status;string result;bool abort=false;
        if(!valid) {status=401;result="Firma inválida o fecha fuera de ventana";}
        else if(item is null) {status=400;result="Contrato inválido";}
        else if(mode=="FallaSimulada") {status=503;result="Falla HTTP simulada";}
        else
        {
            using var previous=Command(db,tx,"SELECT hash FROM events WHERE id=$id",("$id",id));
            var existing=previous.ExecuteScalar() as string;
            var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Payload)));
            if(existing is not null) {status=existing==hash ? 200:409;result=existing==hash ? "Duplicado: sin segundo efecto":"Conflicto: identidad con otro payload";}
            else
            {
                using var count=Command(db,tx,"SELECT count(*) FROM events");
                if((long)count.ExecuteScalar()!>=10000) {status=507;result="Capacidad del ensayo agotada";}
                else
                {
                    using var insert=Command(db,tx,"INSERT INTO events VALUES($id,$type,$version,$shipment,$state,$progress,$occurred,$received,$payload,$hash)",
                        ("$id",item.Id),("$type",item.Type),("$version",item.Version),("$shipment",item.Shipment),("$state",item.State),("$progress",item.Progress),("$occurred",item.OccurredAt),("$received",item.ReceivedAt.ToString("O")),("$payload",item.Payload),("$hash",hash));
                    insert.ExecuteNonQuery();status=200;result="Nuevo: procesado";
                }
            }
            if(mode=="RespuestaPerdida" && status==200)
            {
                abort=true;result+="; conexión interrumpida después de persistir";
                using var restore=Command(db,tx,"UPDATE config SET mode='Disponible' WHERE id=1");restore.ExecuteNonQuery();
            }
        }
        using var attempt=Command(db,tx,"INSERT INTO attempts(event_id,result,valid,received) VALUES($id,$result,$valid,$received)",("$id",id),("$result",result),("$valid",valid?1:0),("$received",DateTimeOffset.UtcNow.ToString("O")));
        attempt.ExecuteNonQuery();
        // Retain deduplication identities for the whole assay; only old HTTP diagnostics are bounded.
        using var trim=Command(db,tx,"DELETE FROM attempts WHERE id <= (SELECT max(id)-2000 FROM attempts)");trim.ExecuteNonQuery();
        tx.Commit();return(status,abort);
    }
    public ReceiverInbox Inbox(int page)
    {
        using var db=Open();using var tx=db.BeginTransaction();
        using var mode=Command(db,tx,"SELECT mode FROM config WHERE id=1");var state=(string)mode.ExecuteScalar()!;
        using var count=Command(db,tx,"SELECT count(*) FROM events");var total=(long)count.ExecuteScalar()!;
        using var attemptsCount=Command(db,tx,"SELECT coalesce(max(id),0) FROM attempts");var attemptTotal=(long)attemptsCount.ExecuteScalar()!;
        var events=new List<ReceiverEvent>();
        using(var cmd=Command(db,tx,"SELECT id,type,version,shipment,state,progress,occurred,received,payload FROM events ORDER BY received DESC,id LIMIT 25 OFFSET $skip",("$skip",(Math.Clamp(page,1,10000)-1)*25)))
        using(var r=cmd.ExecuteReader()) while(r.Read()) events.Add(new(r.GetString(0),r.GetString(1),r.GetInt32(2),r.GetString(3),r.GetString(4),r.GetString(5),r.IsDBNull(6)?null:r.GetString(6),DateTimeOffset.Parse(r.GetString(7)),r.GetString(8)));
        var attempts=new List<ReceiverAttempt>();
        using(var cmd=Command(db,tx,"SELECT id,event_id,result,valid,received FROM attempts ORDER BY id DESC LIMIT 25 OFFSET $skip",("$skip",(Math.Clamp(page,1,10000)-1)*25)))
        using(var r=cmd.ExecuteReader()) while(r.Read()) attempts.Add(new(r.GetInt64(0),r.IsDBNull(1)?null:r.GetString(1),r.GetString(2),r.GetInt32(3)==1,DateTimeOffset.Parse(r.GetString(4))));
        tx.Commit();return new(state,total,attemptTotal,events.ToArray(),attempts.ToArray());
    }
}
