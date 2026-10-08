# Cliente HTTP y contratos de la API

`src/NexoRuta.ApiClient` contiene el cliente tipado `EnviosApiClient` y los DTOs HTTP que comparten Commerce y Backoffice. No referencia proyectos ni entidades del backend. Las pantallas usan sus métodos de consulta y alta; las rutas, serialización y lectura de errores quedan en el cliente.

Las dos aplicaciones registran el cliente con `AddHttpClient<EnviosApiClient>` y configuran su URL mediante `Api:BaseAddress`. Compose define `http://api:8080/`; el valor por defecto para ejecución fuera de Docker es `http://localhost:5000/`.

`Contracts/CrearEnvioRequest.cs` también sirve como modelo del formulario. Conserva las longitudes 160/240/80 de nombre, dirección y código, y el rango `0.01`–`999999999` de peso y dimensiones. La API mantiene su contrato HTTP independiente en `NexoRuta.Api/Contracts/Envios/CrearEnvioRequest.cs`.

El cliente acepta las respuestas JSON de contexto, alta y listado. Para errores HTTP interpreta `message`, Problem Details (`detail`/`title`) y errores de validación (`errors`). Expone `NexoRutaApiException` con un mensaje y el código HTTP; un error HTML o de texto genera un mensaje genérico. Las pantallas no reciben `HttpResponseMessage` ni presentan JSON crudo.

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
