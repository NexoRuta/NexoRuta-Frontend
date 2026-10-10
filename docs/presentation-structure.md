# Feature-first presentation without routing changes

Razor views stay in their native `Pages` root. Feature-owned PageModels live in
`Features/<feature>/Pages` and are linked explicitly with `@model`.
This preserves page identities as well as public URLs: moving only the logic
avoids extra route aliases and changes to `asp-page` or authorization conventions.

## Backoffice: Razor Pages

```text
Features/
  Envios/Pages/IndexModel.cs
  Sesion/Pages/IngresarModel.cs
  Sesion/Pages/SalirModel.cs
Pages/
  Index.cshtml
  Ingresar.cshtml
  Salir.cshtml
  Error.cshtml + Error.cshtml.cs
  Shared/                     # Existing layout and validation partial
  _ViewImports.cshtml
  _ViewStart.cshtml
```

| View identity | Public URL | Feature-owned PageModel |
| --- | --- | --- |
| `/Index` | `/`, `/Index` | `Features/Envios/Pages/IndexModel.cs` |
| `/Ingresar` | `/ingresar` | `Features/Sesion/Pages/IngresarModel.cs` |
| `/Salir` | `/salir` | `Features/Sesion/Pages/SalirModel.cs` |
| `/Error` | `/Error` | Unchanged transversal page |

Keep `AuthorizeFolder("/", Operador)`, anonymous exceptions, cookies, antiforgery,
layout discovery and `/health/ready` unchanged. Layout navigation still addresses
`/Index` and `/Salir`; login forms still address `/Ingresar`.

## Commerce: Blazor and Razor Pages

The Blazor shipment component remains
`Components/Features/Envios/Pages/CrearEnvio.razor`, with route `/` and
`InteractiveServer`. ARQ-05 does not change it.

The Razor views `Pages/Ingresar.cshtml` and `Pages/Salir.cshtml` retain identities
`/Ingresar` and `/Salir`, public URLs `/ingresar` and `/salir`, and their original
forms. Their PageModels live in `Features/Sesion/Pages`.

Authentication, cookie names, claims, redirects, antiforgery and HTTP client
configuration are not moved or replaced. Session helpers remain in ApiClient.

## Tracking: no artificial feature

Tracking currently contains only the Blazor template home page and transversal
error/not-found pages, plus layout and reconnection components. It has no actual
shipment-tracking feature, API client or dedicated service. Keep the existing
`Components/Pages` and `Components/Layout` structure until a functional module
exists; do not create empty feature folders or invent tracking behavior.

Its routes `/`, `/Error` and `/not-found` remain unchanged. It does not register a
health endpoint.

## Verification scope

Run `dotnet restore NexoRuta.sln`, Debug and Release builds, and
`dotnet test NexoRuta.sln --no-build --no-restore` for the matching configuration.
The existing ApiClient suite also verifies the versioned OpenAPI snapshot.
After a Debug build, run `python scripts/check-presentation.py` (Python 3 standard
library only). It starts frontend processes, checks that ports 55140, 55141
and 55142 are free, and stores logs/snapshots outside Git in a temporary directory.
It overrides the backend URL with a loopback simulator and performs no database
operations. Run it with controlled environment variables and without external
HTTP proxies: subprocesses inherit the environment, and the port check is not an
atomic reservation.

Normal cleanup terminates the launched processes, but setup occurs before the
cleanup block and termination does not explicitly cover the entire child process
tree. A startup failure or interruption can therefore leave resources behind;
check the three ports after a failed run. Hardening those failure paths is separate
work, not part of this documentation-only audit. The script asserts current
behavior and records snapshots; it does not automatically compare a retained
before/after baseline.

ARQ-05 additionally compares real frontend HTTP hosts before/after using a
simulated backend: anonymous/authenticated access, `/Index`, login/logout,
required selection, wrong account type, antiforgery rejection, visible content,
form/link targets, cookie attributes and static assets. This is not backend
end-to-end validation and does not use a database. Tracking is checked without
API requests. Snapshot equality excludes generated token and request-ID values.

Commerce already redirects anonymous login POSTs rejected by antiforgery to
`/ingresar?ReturnUrl=%2Fnot-found` through status-code re-execution. The login
handler does not run. This baseline behavior is preserved, not corrected here.
