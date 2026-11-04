namespace NexoRuta.ApiClient.Core.Session;

// Only navigation paths survive recovery. Queries/fragments may contain tokens or form data.
public static class RutaSesion
{
    public static string? Destino(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var path=valor.Split(new[]{'?', '#'},2)[0];
        if(path.Length is 0 or > 512) return null;
        try { path=Uri.UnescapeDataString(path); } catch(UriFormatException) { return null; }
        if(!path.StartsWith('/') || path.StartsWith("//",StringComparison.Ordinal)
            || path.Any(c=>char.IsControl(c)||char.IsWhiteSpace(c)||c is '\\' or '%' or '?' or '#')) return null;
        if(path.Split('/').Any(segment=>segment is "." or ".."))return null;
        var root=path.Split('/')[1];
        if(new[]{"ingresar","salir","api-indisponible","api","restablecer-contrasena","recuperar-contrasena"}.Contains(root,StringComparer.OrdinalIgnoreCase))return null;
        return path;
    }
    public static string Ingreso(string? destino,bool expirada=false,string? ingresoRechazado=null)
        => ConDestino("/ingresar"+(expirada?"?expirada=true":"")
            +(expirada && Guid.TryParseExact(ingresoRechazado,"N",out var id)?"&rechazo="+id.ToString("N"):""),destino);
    public static string Indisponible(string? destino)=>ConDestino("/api-indisponible",destino);
    private static string ConDestino(string pagina,string? destino)
        => Destino(destino) is {} ruta ? pagina+(pagina.Contains('?')?"&":"?")+"ReturnUrl="+Uri.EscapeDataString(ruta) : pagina;
}
