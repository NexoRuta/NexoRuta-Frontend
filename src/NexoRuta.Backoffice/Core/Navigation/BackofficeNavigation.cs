using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.Backoffice.Core.Security;
using NexoRuta.Backoffice.Features.ZonasCobertura;

namespace NexoRuta.Backoffice.Core.Navigation;

public sealed record BackofficeNavigationLink(
    string Label,
    string Href,
    string Page,
    string ActivePagePrefix,
    string? Permission,
    bool ShowOnStandalonePage = false)
{
    public bool IsActive(string? currentPage) =>
        currentPage is not null && currentPage.StartsWith(ActivePagePrefix, StringComparison.Ordinal);
}

public sealed record BackofficeNavigationGroup(string Label, IReadOnlyList<BackofficeNavigationLink> Links);

public enum BackofficeNavigationStatus
{
    Hidden,
    Ready,
    Unavailable
}

public sealed record BackofficeNavigationSnapshot(
    BackofficeNavigationStatus Status,
    IReadOnlyList<BackofficeNavigationGroup> Groups,
    bool Standalone = false)
{
    public static BackofficeNavigationSnapshot Hidden { get; } = new(BackofficeNavigationStatus.Hidden, []);
}

/// <summary>Single source for Backoffice navigation destinations and permission visibility.</summary>
public static class BackofficeNavigation
{
    private static readonly IReadOnlyList<BackofficeNavigationGroup> Catalog =
    [
        new("Operación",
        [
            new("Envíos", "/", "/Index", "/Index", "envios.consultar"),
            new("Rutas y depósito", "/rutas", "/Rutas", "/Rutas", "envios.gestionar")
        ]),
        new("Operador y equipo",
        [
            new("Mi operador", "/mi-operador", "/MiOperador", "/MiOperador", null),
            new("Usuarios", "/usuarios", "/Usuarios", "/Usuarios", "usuarios.gestionar"),
            new("Repartidores", "/repartidores", "/Repartidores", "/Repartidores", "usuarios.gestionar")
        ]),
        new("Configuración",
        [
            new("Configuración de reparto", "/configuracion-reparto", "/ConfiguracionReparto", "/ConfiguracionReparto", ZonaCoberturaUi.PermisoGestionar),
            new("Roles y permisos", "/roles", "/Roles", "/Roles", ZonaCoberturaUi.PermisoGestionar),
            new("Identidad visual", "/identidad-visual", "/IdentidadVisual", "/IdentidadVisual", ZonaCoberturaUi.PermisoGestionar),
            new("Zonas y franjas", "/zonas", "/ZonasCobertura/Index", "/ZonasCobertura", ZonaCoberturaUi.PermisoGestionar, true),
            new("Tarifas", "/tarifas", "/Tarifas/Index", "/Tarifas", ZonaCoberturaUi.PermisoGestionar, true),
            new("Reglas operativas", "/reglas-operativas", "/ReglasOperativas/Index", "/ReglasOperativas", ZonaCoberturaUi.PermisoGestionar, true)
        ])
    ];

    public static async Task<BackofficeNavigationSnapshot> LoadAsync(
        IAuthorizationService authorization,
        ClaimsPrincipal user,
        HttpContext? httpContext,
        bool standalone = false)
    {
        if (user.Identity?.IsAuthenticated != true ||
            user.HasClaim(SesionUsuario.ClaimTipoAcceso, SesionUsuario.OrganizacionSuspendida))
            return BackofficeNavigationSnapshot.Hidden;

        var operatorContext = await authorization.AuthorizeAsync(user, resource: null, SesionUsuario.Operador);
        if (!operatorContext.Succeeded) return BackofficeNavigationSnapshot.Hidden;

        // This policy asks the request-scoped handler to load the current permission set once.
        await authorization.AuthorizeAsync(user, resource: null, ZonaCoberturaUi.PermisoGestionar);
        if (httpContext?.Items.TryGetValue(ApiPermissionAuthorizationHandler.PermissionsCacheKey, out var cached) != true ||
            cached is not IReadOnlyList<string> permissions)
            return new(BackofficeNavigationStatus.Unavailable, [], standalone);

        var groups = Catalog
            .Select(group => new BackofficeNavigationGroup(group.Label, group.Links
                .Where(link => (!standalone || link.ShowOnStandalonePage) &&
                    (link.Permission is null || permissions.Contains(link.Permission, StringComparer.Ordinal)))
                .ToArray()))
            .Where(group => group.Links.Count > 0)
            .ToArray();
        return new(BackofficeNavigationStatus.Ready, groups, standalone);
    }
}
