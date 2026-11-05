using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.ApiClient.Core.Session;
using System.Globalization;
namespace NexoRuta.Backoffice.Pages;
public sealed class ReporteOperacionesModel(EnviosApiClient api) : PageModel
{
    public IReadOnlyList<FilaReporte> Filas { get; private set; } = [];
    public string? Error { get; private set; }
    public async Task OnGetAsync()
    {
        try {
            var envios = await api.ListarEnviosAsync(SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
            Filas = envios.GroupBy(e => new { e.EsRetiro, e.Estado }).Select(g => new FilaReporte(g.Key.EsRetiro ? "Retiro" : "Entrega",g.Key.Estado,g.Count(),g.Sum(e => e.Bultos.Count),g.Sum(e=>e.Bultos.Count(b=>e.EsRetiro ? b.Custodia=="Comercio" : b.Resultado=="Entregado")),g.Sum(e=>e.Tarifa?.TotalFinalUyu??0m))).ToArray();
        } catch(Exception) { Error="No pudimos consultar el reporte. Volvé a intentarlo."; }
    }
    public async Task<IActionResult> OnGetCsvAsync()
    {
        await OnGetAsync(); if(Error!=null) return Page();
        return File(System.Text.Encoding.UTF8.GetBytes("Tipo,Estado,Operaciones,Bultos,Entregados,ImporteUyu\r\n" + string.Join("\r\n",Filas.Select(f=>$"{f.Tipo},{f.Estado},{f.Operaciones},{f.Bultos},{f.BultosEntregados},{f.ImporteUyu.ToString(CultureInfo.InvariantCulture)}"))),"text/csv; charset=utf-8","operaciones.csv");
    }
    public sealed record FilaReporte(string Tipo,string Estado,int Operaciones,int Bultos,int BultosEntregados,decimal ImporteUyu);
}
