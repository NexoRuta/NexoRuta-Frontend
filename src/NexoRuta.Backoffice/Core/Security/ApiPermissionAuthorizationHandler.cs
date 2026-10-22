using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Usuarios.Clients;

namespace NexoRuta.Backoffice.Core.Security;

public sealed record ApiPermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class ApiPermissionAuthorizationHandler(
    UsuariosApiClient usuariosClient,
    IHttpContextAccessor httpContextAccessor) : AuthorizationHandler<ApiPermissionRequirement>
{
    public const string PermissionsCacheKey = "NexoRuta.Backoffice.ApiPermissions";
    public const string ExpiredSessionItemKey = "NexoRuta.Backoffice.ApiSessionExpired";
    public const string ApiUnavailableItemKey = "NexoRuta.Backoffice.ApiUnavailable";

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ApiPermissionRequirement requirement)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null || context.User.Identity?.IsAuthenticated != true) return;

        IReadOnlyList<string> permissions;
        if (httpContext.Items.TryGetValue(PermissionsCacheKey, out var cached) && cached is IReadOnlyList<string> current)
            permissions = current;
        else
        {
            try
            {
                permissions = await usuariosClient.ObtenerPermisosActualesAsync(
                    SesionUsuario.ObtenerToken(context.User), httpContext.RequestAborted);
                httpContext.Items[PermissionsCacheKey] = permissions;
            }
            catch (NexoRutaApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                httpContext.Items[ExpiredSessionItemKey] = true;
                context.Fail();
                return;
            }
            catch (NexoRutaApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                context.Fail();
                return;
            }
            catch (NexoRutaApiException)
            {
                DenyForUnavailableApi(httpContext, context);
                return;
            }
            catch (HttpRequestException)
            {
                DenyForUnavailableApi(httpContext, context);
                return;
            }
            catch (OperationCanceledException) when (!httpContext.RequestAborted.IsCancellationRequested)
            {
                DenyForUnavailableApi(httpContext, context);
                return;
            }
        }

        if (permissions.Contains(requirement.Permission, StringComparer.Ordinal))
            context.Succeed(requirement);
    }

    private static void DenyForUnavailableApi(HttpContext httpContext, AuthorizationHandlerContext context)
    {
        httpContext.Items[ApiUnavailableItemKey] = true;
        context.Fail();
    }
}
