# NexoRuta Frontend

Repositorio de las tres aplicaciones web de NexoRuta: backoffice, portal de comercios y portal de seguimiento. Las tres están construidas con ASP.NET Core sobre .NET 10; Backoffice usa Razor Pages y Commerce/Tracking usan Blazor Server interactivo.

## Estado actual

La solución compila. Las aplicaciones siguen siendo mayormente plantillas .NET: contienen pantallas de ejemplo (incluidos contador y clima en Blazor), sin flujos de negocio ni integración funcional con la API. No se implementan ni se afirman autenticación, pedidos, seguimiento real o CI. Este repositorio no tiene proyectos de pruebas; **se ejecutan cero pruebas**.

## Estructura

```text
NexoRuta.sln
src/
  NexoRuta.Backoffice/  # Razor Pages
  NexoRuta.Commerce/    # Blazor Server interactivo
  NexoRuta.Tracking/    # Blazor Server interactivo
Dockerfile              # publica un proyecto elegido por el argumento PROJECT
```

Cada aplicación contiene su `Program.cs`, configuración `appsettings*.json`, perfil `Properties/launchSettings.json` y páginas/componentes bajo `Pages/` o `Components/`. Los recursos estáticos de Bootstrap, jQuery y validación están en `wwwroot/`.

## Requisitos y compilación

- .NET SDK 10. En el checkout combinado, la raíz del proyecto fija SDK `10.0.401` mediante `global.json`; al clonar este submódulo por separado se necesita un SDK compatible con `net10.0`.
- Docker Desktop/Engine es opcional para ejecutar su imagen. El Compose local está en el repositorio principal.

Desde la raíz de este repositorio:

```bash
dotnet restore NexoRuta.sln --nologo
dotnet build NexoRuta.sln --no-restore --nologo
dotnet test NexoRuta.sln --no-build --no-restore --nologo
```

En la validación local del 7 de octubre de 2026, restore y build finalizaron sin advertencias ni errores. `dotnet test` finalizó con código 0, pero esta solución no contiene proyectos de pruebas: **no ejecutó pruebas**.

## Ejecutar las aplicaciones

Desde la raíz de este repositorio, cada app puede iniciarse de forma independiente:

```bash
dotnet run --project src/NexoRuta.Backoffice/NexoRuta.Backoffice.csproj --launch-profile http
dotnet run --project src/NexoRuta.Commerce/NexoRuta.Commerce.csproj --launch-profile http
dotnet run --project src/NexoRuta.Tracking/NexoRuta.Tracking.csproj --launch-profile http
```

Los perfiles `http` configuran estas direcciones; ejecutá cada comando en una terminal separada:

| Aplicación | URL local | Configuración |
| --- | --- | --- |
| Backoffice | `http://localhost:5046` | `src/NexoRuta.Backoffice/Properties/launchSettings.json` |
| Commerce | `http://localhost:5217` | `src/NexoRuta.Commerce/Properties/launchSettings.json` |
| Tracking | `http://localhost:5298` | `src/NexoRuta.Tracking/Properties/launchSettings.json` |

El Compose del repositorio principal publica las mismas tres apps en `http://localhost:5101`, `http://localhost:5102` y `http://localhost:5103`, respectivamente. Esos puertos de contenedor no sustituyen los perfiles locales anteriores.

## Pendiente

Implementar las pantallas y flujos propios de cada actor, conectar los clientes con endpoints existentes cuando estén disponibles, agregar autenticación/autorización y pruebas automatizadas. El HTTP 200 de una plantilla o su compilación no valida esos flujos.
