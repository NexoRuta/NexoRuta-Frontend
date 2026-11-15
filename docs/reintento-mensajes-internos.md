# Reintento de mensajes internos

Backoffice → Configuración → **Mensajes internos** (`/mensajes-internos`) permite
solicitar el reintento de una publicación fallida del operador actual. Requiere
sesión de operador y los dos permisos: `configuracion.gestionar` y
`envios.consultar`. Es una extensión operativa del sistema existente: reutiliza
Razor Pages, Bootstrap, estilos semánticos y envío único, siguiendo
[DESIGN.md](../DESIGN.md).

La lista muestra 25 mensajes por página, con identificador, tipo, estado,
destino, intentos y diagnóstico seguro. En cada mensaje, **Solicitar reintento**
abre su formulario individual. El motivo es obligatorio y admite hasta 500
caracteres; conviene describir la causa corregida. Durante el envío, el botón
muestra **Solicitando reintento…**, queda deshabilitado y el formulario comunica
que está ocupado.

## Resultados y recuperación

- `Solicitado`: confirma la solicitud; el Worker habitual volverá a publicar.
- `NoElegible` o `NoDisponible`: informa que el mensaje ya no está disponible
  para reintento y que la solicitud quedó registrada.
- Validación o error conocido: muestra el error y conserva la selección y el
  motivo para corregir o repetir la solicitud.
- Resultado incierto por transporte o por una respuesta exitosa ilegible:
  conserva mensaje, identidad de solicitud, intentos y motivo. El motivo queda
  de solo lectura para repetir la misma solicitud con su contenido original.
  Esta conservación también funciona si la consulta posterior de la lista
  devuelve 503; el formulario seleccionado permanece disponible.
- Lista vacía: informa que no hay publicaciones fallidas en esa página. Si la
  consulta falla, ofrece **Reintentar carga**.

La confirmación de publicación no acredita consumo exitoso. Esta pantalla no
recupera fallos de consumidores ni mensajes de DLQ y no reenvía webhooks HTTP.
Esos recorridos conservan sus mecanismos propios. El contrato, la auditoría y
los límites están en la
[documentación backend](../../backend/docs/workflow/reintento-mensajes-internos.md).

## Verificación del 15/11/2026

Las pruebas enfocadas de frontend pasaron **3/3**: dos casos HTTP de resultado
incierto (transporte y 2xx ilegible), ambos seguidos de una consulta 503, y un
recorrido Chromium con API controlada. Comprueban la repetición con la misma
identidad y el rechazo sin ambos permisos.

El recorrido de navegador comprobó escritorio de 1440 px y móvil de 320 px,
apertura y envío por teclado, validación, progreso, confirmación y vacío, sin
desbordamiento horizontal. Para inspeccionar el progreso, detuvo una vez la
acción predeterminada del envío, dejó actuar al handler real y después envió
el mismo formulario. Se revisaron las capturas de escritorio, móvil,
validación, progreso y vacío; son evidencia local, no parte del contrato.

La revisión independiente final dio aprobación tras resolver tres correcciones.
Esta evidencia de UI no certifica conformidad WCAG ni una conexión real con el
broker. Por separado, los dos `AdministrativeOutboxRetryTests` pasaron con
PostgreSQL 18 y RabbitMQ 4 reales, dentro de una corrida backend de 11/11.
Esa validación no acredita entrega SMTP.
