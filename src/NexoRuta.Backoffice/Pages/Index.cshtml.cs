using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient;
using NexoRuta.ApiClient.Contracts;

namespace NexoRuta.Backoffice.Pages;

public class IndexModel(EnviosApiClient apiClient) : PageModel
{
    public IReadOnlyList<EnvioResponse> Envios { get; private set; } = [];
    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            Envios = await apiClient.ListarEnviosAsync(HttpContext.RequestAborted);
        }
        catch (NexoRutaApiException exception)
        {
            Error = exception.Message;
        }
        catch (HttpRequestException)
        {
            Error = "No se pudo consultar el servicio de envíos.";
        }
    }
}
