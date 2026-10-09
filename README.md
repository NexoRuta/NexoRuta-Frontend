# NexoRuta Frontend

Repositorio de las tres aplicaciones web de NexoRuta: backoffice, portal de comercios y portal de seguimiento. Las tres están construidas con ASP.NET Core sobre .NET 10; Backoffice usa Razor Pages y Commerce/Tracking usan Blazor Server interactivo.

## Estado actual

Commerce permite el alta individual de un envío con su bulto y Backoffice consulta los registros persistidos por la API. `/ingresar` selecciona una cuenta propia del comercio en Commerce o una cuenta del operador en Backoffice. El usuario del comercio elige el operador desde el formulario de cada envío; las opciones incluyen todos los operadores registrados y la cuenta se lee de PostgreSQL. La API valida que el operador exista y toma el comercio desde la cuenta, sin exigir un vínculo previo. Las aplicaciones usan cookies independientes y `NexoRuta.ApiClient`. El selector es sin credenciales para este monitoreo; no se implementaron CRUD ni perfiles laborales. Tracking conserva la plantilla Blazor.

La solución incluye pruebas del cliente HTTP, validación del formulario y compatibilidad de contratos con OpenAPI. El workflow de frontend ejecuta build y tests en PRs de `develop` hacia `main`; su presencia no acredita una ejecución remota.

## Estructura

```text
NexoRuta.sln
src/
  NexoRuta.Backoffice/  # Razor Pages
  NexoRuta.Commerce/    # Blazor Server interactivo
  NexoRuta.Tracking/    # Blazor Server interactivo
  NexoRuta.ApiClient/   # cliente HTTP tipado y DTOs compartidos
tests/
  NexoRuta.ApiClient.Tests/
contracts/
  nexoruta.openapi.json # copia del contrato publicado por la API
docs/api-client.md      # uso y verificación de contratos
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

La validación histórica del 7 de octubre de 2026 no ejecutó pruebas. Actualmente `dotnet test` ejecuta las pruebas de `NexoRuta.ApiClient.Tests`, sin requerir PostgreSQL. La [guía del cliente HTTP](docs/api-client.md) explica cómo verificar los DTOs contra el documento OpenAPI de un backend activo.

## Ejecutar las aplicaciones

Desde la raíz de este repositorio, cada app puede iniciarse de forma independiente:

```bash
dotnet run --project src/NexoRuta.Backoffice/NexoRuta.Backoffice.csproj --launch-profile http
dotnet run --project src/NexoRuta.Commerce/NexoRuta.Commerce.csproj --launch-profile http
dotnet run --project src/NexoRuta.Tracking/NexoRuta.Tracking.csproj --launch-profile http
```

Backoffice y Commerce usan `http://localhost:5041/` como API en Development, correspondiente al perfil `http` del backend. Para usar la API de Compose con estas webs ejecutadas fuera de Docker, definir `Api__BaseAddress=http://localhost:5000/`.

Los perfiles `http` configuran estas direcciones; ejecutá cada comando en una terminal separada:

| Aplicación | URL local | Configuración |
| --- | --- | --- |
| Backoffice | `http://localhost:5046` | `src/NexoRuta.Backoffice/Properties/launchSettings.json` |
| Commerce | `http://localhost:5217` | `src/NexoRuta.Commerce/Properties/launchSettings.json` |
| Tracking | `http://localhost:5298` | `src/NexoRuta.Tracking/Properties/launchSettings.json` |

El Compose del repositorio principal publica las mismas tres apps en `http://localhost:5101`, `http://localhost:5102` y `http://localhost:5103`, respectivamente. Esos puertos de contenedor no sustituyen los perfiles locales anteriores.

## Pendiente

Completar las pantallas y flujos propios de cada actor, incorporar autenticación por credenciales y permisos por perfil laboral, integrar Tracking y ampliar las pruebas de interfaz y de extremo a extremo. Las pruebas del cliente HTTP y los contratos no validan por sí solas persistencia, aislamiento entre operadores ni flujos completos de usuario.
