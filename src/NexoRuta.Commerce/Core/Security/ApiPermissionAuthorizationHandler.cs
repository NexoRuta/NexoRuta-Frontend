using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Usuarios.Clients;

namespace NexoRuta.Commerce.Core.Security;

public sealed record ApiPermissionRequirement(string Permission) : IAuthorizationRequirement;

public enum CommerceAuthorizationFailure
{
    None,
    Forbidden,
    SessionExpired,
    ApiUnavailable
}

public sealed class CommerceAuthorizationState
{
    public CommerceAuthorizationFailure Failure { get; private set; }

    public void SetFailure(CommerceAuthorizationFailure failure) => Failure = failure;
    public void Clear() => Failure = CommerceAuthorizationFailure.None;
}

public sealed class ApiPermissionAuthorizationHandler(
    UsuariosApiClient usuariosClient,
    IHttpContextAccessor httpContextAccessor,
    CommerceAuthorizationState authorizationState) : AuthorizationHandler<ApiPermissionRequirement>
{
    public const string UsersManagePolicy = "commerce.usuarios.gestionar";
    public const string ConfigManagePolicy = "commerce.configuracion.gestionar";
    public const string ShipmentsCreatePolicy = "commerce.envios.crear";
    public const string ShipmentsReadPolicy = "commerce.envios.consultar";
    public const string PermissionsCacheKey = "NexoRuta.Commerce.ApiPermissions";
    public const string ExpiredSessionItemKey = "NexoRuta.Commerce.ApiSessionExpired";
    public const string ApiUnavailableItemKey = "NexoRuta.Commerce.ApiUnavailable";

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ApiPermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;

        var httpContext = httpContextAccessor.HttpContext;
        IReadOnlyList<string> permissions;
        if (httpContext?.Items.TryGetValue(PermissionsCacheKey, out var cached) == true && cached is IReadOnlyList<string> current)
            permissions = current;
        else
        {
            try
            {
                permissions = await usuariosClient.ObtenerPermisosActualesAsync(
                    SesionUsuario.ObtenerToken(context.User),
                    httpContext?.RequestAborted ?? CancellationToken.None);
                if (httpContext is not null) httpContext.Items[PermissionsCacheKey] = permissions;
            }
            catch (InvalidOperationException)
            {
                DenyAsExpiredSession(httpContext, context);
                return;
            }
            catch (NexoRutaApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                DenyAsExpiredSession(httpContext, context);
                return;
            }
            catch (NexoRutaApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                authorizationState.SetFailure(CommerceAuthorizationFailure.Forbidden);
                context.Fail();
                return;
            }
            catch (NexoRutaApiException)
            {
                DenyAsUnavailable(httpContext, context);
                return;
            }
            catch (HttpRequestException)
            {
                DenyAsUnavailable(httpContext, context);
                return;
            }
            catch (OperationCanceledException) when (httpContext?.RequestAborted.IsCancellationRequested != true)
            {
                DenyAsUnavailable(httpContext, context);
                return;
            }
        }

        authorizationState.Clear();
        if (permissions.Contains(requirement.Permission, StringComparer.Ordinal))
            context.Succeed(requirement);
        else
        {
            authorizationState.SetFailure(CommerceAuthorizationFailure.Forbidden);
            context.Fail();
        }
    }

    private void DenyAsExpiredSession(HttpContext? httpContext, AuthorizationHandlerContext context)
    {
        authorizationState.SetFailure(CommerceAuthorizationFailure.SessionExpired);
        if (httpContext is not null) httpContext.Items[ExpiredSessionItemKey] = true;
        context.Fail();
    }

    private void DenyAsUnavailable(HttpContext? httpContext, AuthorizationHandlerContext context)
    {
        authorizationState.SetFailure(CommerceAuthorizationFailure.ApiUnavailable);
        if (httpContext is not null) httpContext.Items[ApiUnavailableItemKey] = true;
        context.Fail();
    }
}
