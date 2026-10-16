# Cliente HTTP y contratos de la API

`src/NexoRuta.ApiClient` organiza los clientes tipados y contratos HTTP por feature: `AuthApiClient` inicia sesión, `UsuariosApiClient` consulta el usuario/contexto actual y gestiona usuarios/accesos, `RolesApiClient` gestiona roles y permisos, `EnviosApiClient` conserva crear/listar envíos y `OperadoresApiClient` lista operadores. Requests y Responses se separan bajo `Features/<feature>/Contracts`; `Core/Http/ApiHttp` comparte envío, lectura JSON y errores, y `Core/Session/SesionUsuario` crea la identidad local independiente de cada aplicación. No referencia proyectos ni entidades del backend.

Las dos aplicaciones configuran una única URL mediante `Api:BaseAddress` en `AddHttpClient("NexoRuta", ...)` y enlazan sus clientes con `AddTypedClient<T>()`. Commerce registra Auth, usuarios, envíos y operadores; Backoffice registra Auth, usuarios, roles y envíos. Los clientes colocan el Bearer en cada `HttpRequestMessage`; no modifican `DefaultRequestHeaders` compartidos. Tracking no consume ApiClient actualmente. Compose define `http://api:8080/`. Con los perfiles `http` de `dotnet run`, Development usa `http://localhost:5041/`, igual que el perfil local de la API. Para ejecutar las webs fuera de Docker contra la API de Compose, definir `Api__BaseAddress=http://localhost:5000/`; la misma variable permite elegir otra URL.

`Features/Envios/Contracts/Requests/CrearEnvioRequest.cs` también sirve como modelo del formulario. Conserva las longitudes 160/240/80 de nombre, dirección y código, y el rango `0.01`–`999999999` de peso y dimensiones. La API mantiene su contrato HTTP independiente en `NexoRuta.Api/Features/Envios/Contracts/Requests/CrearEnvioRequest.cs`.

`GET /api/accesos` y la autenticación `X-NexoRuta-Acceso` fueron retirados: la identidad no se elige desde un catálogo global. `AuthApiClient.LoginAsync` envía correo y contraseña a `POST /api/auth/login`; las llamadas autenticadas reciben el JWT explícitamente desde la sesión local y agregan `Authorization: Bearer` solo al request correspondiente. `UsuariosApiClient` consulta `GET /api/usuarios/actual`, `GET /api/usuarios/actual/permisos`, el listado/alta de usuarios y `PATCH /api/usuarios/{usuarioId}/estado`. `RolesApiClient` consume `/api/roles`, `/api/roles/asignables`, `/api/roles/permisos`, asignación de rol a acceso y operaciones de rol/permisos.

El ingreso a Commerce y Backoffice valida el JWT llamando luego a `GET /api/usuarios/actual`; el tipo de acceso determina a qué portal pertenece la cuenta. `ListarOperadoresAsync(token)` consulta `GET /api/comercio/operadores` para poblar el dropdown del formulario de alta con todos los operadores registrados. `CrearEnvioRequest.OperadorId` es obligatorio y cambia por envío; la API valida que el operador exista, sin exigir una relación previa con el comercio. La cuenta y el comercio de origen no cambian al elegir otro operador. `UsuarioActualResponse.OperadorId` es nulo para cuentas del comercio y `ComercioId` es nulo para cuentas del operador.

Cada app guarda el token como un claim de su ticket de autenticación protegido por ASP.NET Core Cookie Authentication; el ticket no es persistente, HttpOnly, Secure fuera de Development, y no se escribe en localStorage ni se renderiza al navegador. Los formularios Razor Pages usan antiforgery y logout es un POST. Un 401 de API invalida la sesión local y lleva a `/ingresar?expirada=true`; no se incorporan refresh tokens. La API comprueba estado y permisos dinámicos en cada llamada. La migración futura a ASP.NET Identity/OIDC sigue siendo deuda explícita, sin implementación en este corte.

El resumen de usuario de organización conserva separados `UsuarioId`, `AccesoId`, `Email`, el rol asignado y `Activo`; el cambio de estado desactiva un acceso, no elimina la identidad. El modelo actual asigna cero o un rol por acceso. Los roles llevan los códigos de permisos definidos por el catálogo; el catálogo no se edita desde Backoffice. Para errores HTTP el cliente interpreta `message`, Problem Details (`detail`/`title`) y errores de validación (`errors`). Expone `NexoRutaApiException` con mensaje y código HTTP; HTML/texto inesperado produce un mensaje genérico.

Todos los métodos del cliente aceptan `CancellationToken` y lo propagan al envío HTTP y a la lectura de la respuesta. Las Razor Pages pasan `HttpContext.RequestAborted`. El componente `CrearEnvio` de Commerce (`Components/Features/Envios/Pages/CrearEnvio.razor`) usa su propio `CancellationTokenSource` para obtener la cuenta, listar operadores y crear el envío; al destruirse cancela y libera ese recurso. Trata la cancelación por salida del componente sin mostrar un error, y conserva el tratamiento de los errores HTTP. Cancelar una petición no deshace un envío que la API ya haya confirmado.

La elegibilidad actual de operadores se conserva temporalmente: no resuelve D01 ni define la regla definitiva de negocio. El refactor mantiene contratos HTTP independientes y valida los limites decimales con cultura invariante. Toda la suite xUnit usa `es-UY` mediante `xunit.runner.json`.

## Comprobar la compatibilidad

Desde la raíz del frontend:

```bash
dotnet test NexoRuta.sln --configuration Release
```

Las pruebas usan un transporte HTTP simulado y la copia versionada `contracts/nexoruta.openapi.json`. Comprueban el JSON enviado, las respuestas y errores, los límites del formulario, las rutas y la compatibilidad de nombres/tipos de los DTOs con OpenAPI. No escriben envíos ni necesitan una base de datos.

Para comparar con el backend que esté corriendo, usar su documento actual:

```bash
NEXORUTA_OPENAPI_URL=http://localhost:5000/openapi/v1.json \
  dotnet test NexoRuta.sln --configuration Release
```

La variable debe contener la URL completa del documento OpenAPI. Sin ella se prueba contra la copia versionada, que no detecta por sí sola cambios posteriores en otro repositorio. La API publica OpenAPI únicamente en Development.

Si se modifica el contrato del backend, primero ejecutar las pruebas contra la API actual; después actualizar la copia y repetirlas:

```bash
curl --fail --silent --show-error http://localhost:5000/openapi/v1.json \
  --output contracts/nexoruta.openapi.json
dotnet test NexoRuta.sln --configuration Release
```

La copia contiene metadatos de la API, sin datos de usuarios o envíos. Los DTOs se mantienen explícitamente: no se genera código durante el build ni se requiere un backend activo en CI.
