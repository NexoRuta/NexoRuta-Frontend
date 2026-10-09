# Cliente HTTP y contratos de la API

`src/NexoRuta.ApiClient` contiene el cliente tipado `EnviosApiClient` y los DTOs HTTP que comparten Commerce y Backoffice. No referencia proyectos ni entidades del backend. Las pantallas usan sus métodos de consulta y alta; las rutas, serialización y lectura de errores quedan en el cliente.

Las dos aplicaciones registran el cliente con `AddHttpClient<EnviosApiClient>` y configuran su URL mediante `Api:BaseAddress`. Compose define `http://api:8080/`. Con los perfiles `http` de `dotnet run`, Development usa `http://localhost:5041/`, igual que el perfil local de la API. Para ejecutar las webs fuera de Docker contra la API de Compose, definir `Api__BaseAddress=http://localhost:5000/`; la misma variable permite elegir otra URL.

`Contracts/CrearEnvioRequest.cs` también sirve como modelo del formulario. Conserva las longitudes 160/240/80 de nombre, dirección y código, y el rango `0.01`–`999999999` de peso y dimensiones. La API mantiene su contrato HTTP independiente en `NexoRuta.Api/Contracts/Envios/CrearEnvioRequest.cs`.

`ListarAccesosAsync(tipo)` consulta `GET /api/accesos?tipo=Comercio|Operador` para poblar el selector de cuentas desde la base de datos. `ObtenerUsuarioActualAsync(accesoId)`, `ListarEnviosAsync(accesoId)` y `CrearEnvioAsync(envio, accesoId)` identifican la cuenta en `X-NexoRuta-Acceso`; la API vuelve a comprobar su pertenencia al comercio u operador.

El ingreso al portal selecciona la cuenta del comercio. `ListarOperadoresAsync(accesoId)` consulta `GET /api/comercio/operadores` para poblar el dropdown del formulario de alta con todos los operadores registrados. `CrearEnvioRequest.OperadorId` es obligatorio y cambia por envío; la API valida que el operador exista, sin exigir una relación previa con el comercio. La cuenta y el comercio de origen no cambian al elegir otro operador. `UsuarioActualResponse.OperadorId` es nulo para cuentas del comercio y `ComercioId` es nulo para cuentas del operador; `EsPropietario` identifica al dueño inicial.

Las pantallas obtienen el ID de cuenta de la cookie de su aplicación mediante `SesionUsuario`. Commerce admite cuentas propias del comercio y Backoffice cuentas del operador usando `nexoruta:tipo_acceso`; esto no representa los perfiles laborales de la letra. Cada aplicación tiene cookies de sesión/antiforgery propias y permite cambiar de usuario mediante un POST. La selección es sin credenciales para el primer monitoreo, y su esquema debe sustituirse al incorporar autenticación real.

Las respuestas de alta y listado incluyen `comercioId` junto a `operadorId`; las opciones del selector solo incluyen `operadorId` y `nombre`. El cliente acepta las respuestas JSON de usuario actual, alta y listado. Para errores HTTP interpreta `message`, Problem Details (`detail`/`title`) y errores de validación (`errors`). Expone `NexoRutaApiException`, agrupada en `NexoRuta.ApiClient/Excepciones`, con un mensaje y el código HTTP; un error HTML o de texto genera un mensaje genérico. Las pantallas no reciben `HttpResponseMessage` ni presentan JSON crudo.

Todos los métodos del cliente aceptan `CancellationToken` y lo propagan al envío HTTP y a la lectura de la respuesta. Las Razor Pages pasan `HttpContext.RequestAborted`. El componente `Home` de Commerce usa su propio `CancellationTokenSource` para obtener la cuenta, listar operadores y crear el envío; al destruirse cancela y libera ese recurso. Trata la cancelación por salida del componente sin mostrar un error, y conserva el tratamiento de los errores HTTP. Cancelar una petición no deshace un envío que la API ya haya confirmado.

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
