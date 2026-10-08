using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http.Json;

namespace NexoRuta.Backoffice.Pages;

public class IndexModel : PageModel
{
    private readonly IHttpClientFactory httpClientFactory;

    public IndexModel(IHttpClientFactory httpClientFactory) => this.httpClientFactory = httpClientFactory;

    public IReadOnlyList<EnvioView> Envios { get; private set; } = [];
    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            Envios = await httpClientFactory.CreateClient("NexoRuta.Api")
                .GetFromJsonAsync<List<EnvioView>>("api/envios") ?? [];
        }
        catch (HttpRequestException)
        {
            Error = "No se pudo consultar la API. Verificá que esté saludable.";
        }
    }

    public sealed record BultoView(string codigo, decimal pesoGramos, decimal largoCentimetros, decimal anchoCentimetros, decimal altoCentimetros);
    public sealed record EnvioView(Guid id, Guid operadorId, Guid operadorComercioId, Guid creadoPorUsuarioId,
        string usuarioEmail, string operadorNombre, string comercioNombre, string destinatarioNombre,
        string direccion, string estado, IReadOnlyList<BultoView> bultos);
}
