# Run the independent platform Admin portal

`src/NexoRuta.Admin` is a fourth web application, alongside Backoffice, Commerce and Tracking. It uses .NET 10 Razor Pages, feature-owned PageModels and the shared `NexoRuta.ApiClient`. It is **not another API** and is only the platform-support portion of CU-01; the adopted Commerce self-service model and traceability matrix are in the [current audit](../../backend/docs/cu/CU-01-comercios-usuarios.md).

## Run locally

From the frontend repository:

```powershell
dotnet build NexoRuta.sln --configuration Release
dotnet run --project src/NexoRuta.Admin --configuration Release --launch-profile http
```

Open **http://localhost:5104/ingresar**. Port 5104 was checked free during implementation. The default API address is `http://localhost:5041/`, matching the backend's HTTP launch profile. To use the existing Compose API instead, set `Api__BaseAddress=http://localhost:5000/` in the portal process environment. The API URL is server-side configuration, never a browser credential or database connection.

In the combined repository, Compose includes `admin`, bound only to `127.0.0.1:5104`, targeting the internal API at `http://api:8080/`:

```powershell
# Confirm the existing local API is running, without initialization enabled.
# Start only this new portal; do not start or reconfigure persistent infrastructure.
docker compose up --build --no-deps admin

# Optional existing watch workflow, with the same no-dependency boundary:
docker compose -f compose.yaml -f compose.dev.yaml up --no-deps admin
```

`Dockerfile` accepts `PROJECT=NexoRuta.Admin`; Admin is in `NexoRuta.sln`, so the existing CI Release build/test covers it. No remote deployment or release publication is performed. Local platform initialization is described below; existing organizational data is preserved. Keep `DatabaseInitialization:ApplyMigrations=false` and `SeedData=false`; their effective local Compose values were checked as false.

## Prepare local platform access

The portal cannot bootstrap an account. The local Compose API supports the explicitly enabled `DatosInicialesSeeder` platform bootstrap, independent of demonstration seeding. In the ignored root `.env`, configure `NEXORUTA_ADMIN_MAIL`, `NEXORUTA_ADMIN_PASSWORD` and `NEXORUTA_PLATFORM_BOOTSTRAP=true`; never commit or print credentials. Keep general `SeedData=false` and habitual `ApplyMigrations=false`.

Before starting the updated API, explicitly review/apply `20261016035056_AddPlatformAdministration` and `20261016045116_AddPlatformRolePermissions` to the confirmed local Compose database. Preserve all volumes and data. API startup then initializes the platform account atomically or validates it without changing its password. Organizational identities are never elevated. No manual `platform-admin create` is needed locally; the interactive command remains a controlled alternative for other explicitly authorized setups. See `backend/docs/workflow/platform-administration.md` for migration, configuration and stale-image diagnostics.

## Routes and project boundaries

| Route | Purpose |
| --- | --- |
| `/ingresar` | Platform credential login |
| `/` | Navigation to operators and commerces |
| `/operadores`, `/comercios` | Paginated lists (`pagina`, `tamanoPagina`) |
| `/operadores/crear`, `/comercios/crear` | Name and first administrator credentials, with confirmation |
| `/operadores/{id}`, `/comercios/{id}` | ID/name detail or controlled not-found state |
| `/acceso-denegado`, `/api-indisponible`, `/error` | Controlled denied/unavailable/error states |
| POST `/salir` | Antiforgery-protected logout; GET never logs out |
| `/health/ready` | Existing frontend liveness/readiness convention |

```text
NexoRuta.Admin/
  Program.cs                        # Cookie, authorization, clients and HTTP pipeline
  Core/Security/                    # Live API permission check and session failure handling
  Features/Sesion/Pages/            # Sign-in and sign-out PageModels
  Features/Organizaciones/Pages/    # List/create/detail PageModels
  Pages/                           # Razor views and shared layout
  wwwroot/                         # Existing vendored Bootstrap + its license, single-submit JS, local styles
```

The three organization views serve both resource types through constrained native Razor routes. The resource type is taken from route data, not posted organization IDs or permission fields. There is no edit/delete UI, operator/commerce ownership relationship, user impersonation, or administrator auto-login. Detail screens render only ID and name because that is the real API response. Stable name/UUID ordering remains owned by the backend; the UI preserves its order and page metadata. Defaults are page 1, size 20, with size 1–100.

## API and authorization

`Features/Plataforma/Clients/OrganizacionesAdminApiClient` uses existing `ApiHttp` to call only the two administrative resources. Requests/responses live under `Features/Plataforma/Contracts/{Requests,Responses}`. POST sends `{ nombre, administrador: { email, password } }`; confirmation never leaves the portal. No backend assembly is referenced.

Login reuses `AuthApiClient` and `UsuariosApiClient`: authenticate, fetch `/api/usuarios/actual`, require exact `Tipo=Plataforma` with no operator/commerce membership, then fetch `/api/usuarios/actual/permisos` and require `plataforma.organizaciones.gestionar`. Role names and codes never authorize. Every protected page request checks current API permissions again, cached only within that HTTP request. The API remains the effective authority for every read/write; a permission failure/outage is never treated as approval.

The existing backend self-context reads were extended compatibly to support platform identity without constructing an organizational `ContextoUsuario`. Platform responses explicitly contain `tipo: "Plataforma"` and null organizational/role fields; organizational responses preserve their values. The shared frontend DTO accepts nullable organizational-role fields. This does not permit platform tokens to use shipment, user-management, role-management or Commerce operator-selection endpoints. Existing Backoffice/Commerce login rejects the platform type.

Cookie `NexoRuta.Admin` is protected by a dedicated data-protection application name, HttpOnly, nonpersistent, SameSite=Lax, non-sliding, and expires no later than the login API's JWT expiry. It is Secure outside Development. Antiforgery has its own cookie name too. Server-side API requests attach Bearer per request; JWTs are not rendered into HTML or stored in localStorage. Local-only return URLs use `Url.IsLocalUrl`/`LocalRedirect`. Rejected login attempts clear any previous Admin session without clearing another portal's cookie. Logout remains available when the platform permission is revoked or its service is down.

Creation validates the model's name/email/password limits and password confirmation. Password values are cleared from PageModel/ModelState before returning an error view, while validation messages are retained, and password inputs have no value binding. API error text is not echoed into credential forms. Safe fields remain after validation, conflict or service failure. Successful creation uses POST/Redirect/GET and a protected TempData confirmation; no new administrator token is requested. Existing single-submit JS suppresses repeated submits while pending. This is not an idempotency key: after timeout/transport failure, check the list before retrying because the server may already have committed.

| API outcome | Portal behavior |
| --- | --- |
| 400 | Safe validation feedback; no password re-render |
| 401 | End Admin session and return to sign-in |
| 403 / missing permission | Access denied; no privilege inferred |
| Detail 404 | Not-found page with HTTP 404 |
| Create 409 | Conflict feedback, safe fields preserved |
| Transport / timeout / server failure | Controlled unavailability; safe form fields retained for create |

## Verification and limitations

```powershell
dotnet build NexoRuta.sln --configuration Release
dotnet test NexoRuta.sln --configuration Release --no-build
git diff --check
```

Tests exercise the real Razor/cookie/antiforgery pipeline through TestServer with an in-memory HTTP API double. They cover both resources, return URLs, expiry, current permission failures, cross-portal cookie isolation, safe passwords, error states, PRG and no impersonation. Contract tests compare the DTOs with the real backend OpenAPI, then its refreshed versioned snapshot. Backend self-context/security regression tests use fixture-owned ephemeral PostgreSQL, never local persistent PostgreSQL.

**Historical verification note:** the original Admin slice had HTTP tests but no visual/browser run. The current CU-01 audit records its own browser/HTTP evidence separately. Markup includes responsive Bootstrap layout, labelled fields, semantic tables, skip navigation and visible keyboard focus.

Identity's EF Core store, email management and PostgreSQL RLS now exist in the current checkout. Admin remains the platform support portal; Commerce self-manages its own users/roles and Backoffice manages the operator's. Physical deletion, password recovery and remote Admin publishing/production routing/TLS are not claimed as CU-01 requirements; consult the [current CU-01 matrix](../../backend/docs/cu/CU-01-comercios-usuarios.md).
