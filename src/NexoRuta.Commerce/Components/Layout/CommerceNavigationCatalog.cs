using NexoRuta.Commerce.Core.Security;

namespace NexoRuta.Commerce.Components.Layout;

public sealed record CommerceNavigationLink(string Label, string Href, string ActivePath, string? PermissionPolicy);

public sealed record CommerceNavigationGroup(string Label, IReadOnlyList<CommerceNavigationLink> Links);

/// <summary>Single source for Commerce navigation destinations and permission visibility.</summary>
public static class CommerceNavigationCatalog
{
    public static IReadOnlyList<CommerceNavigationGroup> Groups { get; } =
    [
        new("Inicio", [new("Inicio", "/", "/", null)]),
        new("Envíos",
        [
            new("Crear envío", "/envios/crear", "/envios/crear", ApiPermissionAuthorizationHandler.ShipmentsCreatePolicy),
            new("Importar CSV", "/envios/importar", "/envios/importar", ApiPermissionAuthorizationHandler.ShipmentsCreatePolicy),
            new("Mis envíos", "/envios", "/envios", ApiPermissionAuthorizationHandler.ShipmentsReadPolicy)
        ]),
        new("Comercio", [new("Mi comercio", "/mi-comercio", "/mi-comercio", null)]),
        new("Administración",
        [
            new("Usuarios", "/usuarios", "/usuarios", ApiPermissionAuthorizationHandler.UsersManagePolicy),
            new("Roles y permisos", "/roles", "/roles", ApiPermissionAuthorizationHandler.ConfigManagePolicy)
        ])
    ];
}
