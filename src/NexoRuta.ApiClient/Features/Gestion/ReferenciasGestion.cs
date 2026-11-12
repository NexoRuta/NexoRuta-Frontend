namespace NexoRuta.ApiClient.Features.Gestion;

// Request-local names only; never a shared cache between tenants.
public sealed class ReferenciasGestion(GestionApiClient api)
{
    private readonly Dictionary<(string,Guid),string> nombres=[];
    public string Nombre(string tipo,Guid? id)=>id is {} value ? nombres.GetValueOrDefault((tipo,value),"Referencia no disponible") : "Sin autor verificable";
    public async Task Cargar(Guid op,string tipo,IEnumerable<Guid> ids,string token,CancellationToken ct)
    {
        foreach(var batch in ids.Distinct().Chunk(100))
        {
            var opciones=await api.NombresAsync(op,tipo,batch,token,ct);
            foreach(var o in opciones.Opciones)nombres[(tipo,o.Id)]=o.Nombre;
        }
    }
}
public sealed record SelectorGestion(string Campo,string Etiqueta,Guid? Seleccion,OpcionesGestion Opciones,bool Obligatorio=false);
public static class EtiquetasGestion
{
    public static string Texto(string valor)=>System.Text.RegularExpressions.Regex.Replace(valor,"(?<=[a-záéíóúñ])(?=[A-ZÁÉÍÓÚÑ])"," ");
    public static string Documento(LiquidacionResumen d)=>$"{d.Estado} · {PeriodoGestion.Fecha(d.PreparadaEnUtc)} · {d.TotalUyu:N2} UYU";
}
