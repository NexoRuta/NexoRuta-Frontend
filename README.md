# NexoRuta Frontend

Repositorio de las tres aplicaciones web de NexoRuta: backoffice, portal de comercios y portal de seguimiento. Las tres están construidas con ASP.NET Core sobre .NET 10; Backoffice usa Razor Pages, Commerce usa componentes Blazor con InteractiveServer y Tracking consulta el estado público de un envío mediante un enlace anónimo.

## Estado actual

Commerce permite el alta individual de un envío con su bulto y Backoffice consulta los registros persistidos por la API. `/ingresar` selecciona una cuenta propia del comercio en Commerce o una cuenta del operador en Backoffice. El usuario del comercio elige el operador desde el formulario de cada envío; las opciones incluyen todos los operadores registrados y la cuenta se lee de PostgreSQL. La API valida que el operador exista y toma el comercio desde la cuenta, sin exigir un vínculo previo. Las aplicaciones usan cookies independientes y `NexoRuta.ApiClient`. El selector es sin credenciales para este monitoreo; no se implementaron CRUD ni perfiles laborales. Commerce lista sus envíos en `/envios` y recupera el seguimiento bajo demanda; Tracking muestra únicamente el estado público.

La solución incluye pruebas del cliente HTTP, validación del formulario y compatibilidad de contratos con OpenAPI. El workflow de frontend ejecuta build y tests en PRs de `develop` hacia `main`; su presencia no acredita una ejecución remota.

## Estructura

```text
NexoRuta.sln
src/
  NexoRuta.Backoffice/  # Razor Pages
  NexoRuta.Commerce/    # Blazor Server interactivo
  NexoRuta.Tracking/    # seguimiento público Blazor
  NexoRuta.ApiClient/   # clientes especializados y contratos por feature
tests/
  NexoRuta.ApiClient.Tests/
contracts/
  nexoruta.openapi.json # copia del contrato publicado por la API
docs/api-client.md      # uso y verificación de contratos
Dockerfile              # publica un proyecto elegido por el argumento PROJECT
```

Cada aplicación contiene su `Program.cs`, configuración `appsettings*.json`, perfil `Properties/launchSettings.json` y páginas/componentes bajo `Pages/` o `Components/`. Los recursos estáticos de Bootstrap, jQuery y validación están en `wwwroot/`.

## Organización de presentación

ApiClient agrupa contratos Request/Response y clientes por feature; `Core` contiene
HTTP, excepciones y sesión compartidos. Commerce mantiene su pantalla en
`Components/Features/Envios/Pages/CrearEnvio.razor`, con ruta `/` e InteractiveServer.
Backoffice y Commerce conservan las vistas Razor en `Pages` y trasladan solamente
sus PageModels a `Features/Envios` o `Features/Sesion`, preservando las identidades
utilizadas por `asp-page`. Tracking no incorpora carpetas funcionales artificiales.

La [guía de presentación](docs/presentation-structure.md) detalla rutas y límites
de las comprobaciones HTTP. Después de un build Debug puede ejecutarse
`python scripts/check-presentation.py` con Python 3, puertos loopback libres y un
entorno controlado sin proxies externos. Usa un backend simulado, no PostgreSQL;
no constituye una prueba end-to-end del sistema.

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

Las tres aplicaciones usan `http://localhost:5041/` como API en Development, correspondiente al perfil `http` del backend. Para usar otra API con estas webs, definir `Api__BaseAddress` en cada terminal.

Los perfiles `http` configuran estas direcciones; ejecutá cada comando en una terminal separada:

| Aplicación | URL local | Configuración |
| --- | --- | --- |
| Backoffice | `http://localhost:5046` | `src/NexoRuta.Backoffice/Properties/launchSettings.json` |
| Commerce | `http://localhost:5217` | `src/NexoRuta.Commerce/Properties/launchSettings.json` |
| Tracking | `http://localhost:5298` | `src/NexoRuta.Tracking/Properties/launchSettings.json` |

El Compose del repositorio principal publica las mismas tres apps en `http://localhost:5101`, `http://localhost:5102` y `http://localhost:5103`, respectivamente. Esos puertos de contenedor no sustituyen los perfiles locales anteriores.

## M05: Commerce → Tracking

Con la API M05 y su Worker existentes configurados y en ejecución, iniciar las
webs en dos terminales PowerShell desde este worktree:

```powershell
# Terminal Commerce
$env:Api__BaseAddress = "http://localhost:5041/"
$env:Tracking__BaseAddress = "http://localhost:5298/"
dotnet run --project src/NexoRuta.Commerce --launch-profile http

# Terminal Tracking
$env:Api__BaseAddress = "http://localhost:5041/"
dotnet run --project src/NexoRuta.Tracking --launch-profile http
```

1. Abrir `http://localhost:5217/ingresar` y seleccionar la cuenta demo del comercio.
2. Crear un envío, entrar a **Mis envíos** (`/envios`) y pulsar **Ver seguimiento**.
3. Si el Worker todavía no generó el enlace, se indica que hay que reintentar.
4. Un enlace vigente abre `http://localhost:5298/seguimiento/{token}` en la misma
   pestaña. Tracking consulta el estado sin enviar identidad ni requerir login.
5. Abrir ese enlace en una ventana privada también debe mostrar únicamente el
   estado. Un enlace inválido, expirado o revocado muestra un mensaje común.

La recuperación interna usa `GET /api/envios/{envioId}/seguimiento` con la
identidad demo existente y sigue restringida a **Development en la API**.
Commerce adapta la URL de API devuelta a `Tracking:BaseAddress`, preservando el
token original y validando estado y expiración. No genera ni almacena tokens.
Tracking mantiene `GET /api/public/seguimiento/{token}` y no modifica su contrato.
Para otro puerto de Tracking, cambiar `Tracking__BaseAddress` en Commerce; la
clave de cifrado compartida API/Worker debe ser la ya configurada en el backend,
no una clave nueva creada por el frontend.

Comprobaciones sin base de datos ni servicios de mensajería:

```powershell
dotnet build NexoRuta.sln --nologo
dotnet test NexoRuta.sln --nologo
python scripts/check-presentation.py
```

Las pruebas incluyen los clientes HTTP y los componentes reales de Commerce y
Tracking con API simulada. El script comprueba hosts, sesión, listado y cabeceras
de la ruta pública; no ejecuta un circuito con PostgreSQL/RabbitMQ reales.

## Pendiente

Completar las pantallas y flujos propios de cada actor, incorporar autenticación por credenciales y permisos por perfil laboral y ampliar las pruebas de extremo a extremo. Las pruebas con API simulada no validan por sí solas persistencia ni servicios de mensajería reales.
