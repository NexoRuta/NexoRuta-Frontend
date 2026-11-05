# Interfaces de entrega parcial y retiro

Se reutilizan Razor Pages, Blazor, Bootstrap y `shared/portal-design.css`. No se agrega otro stack ni una segunda fuente de tokens.

- Commerce: solicitud en `/retiros/crear`, reutilizando destino, bultos, cobertura y cotización; franja explícita en UTC y motivo. Avisos del consumidor en `/avisos`.
- Backoffice: política parcial en el borrador de reglas; modalidad Retiro en el tarifario; entregas y retiros en planificación; máximo de carga por orden. La ruta distingue visita completada, suspendida y retirada. Depósito recibe los retiros y confirma entrega al comercio; también confirma devoluciones parciales.
- Tablero de envíos: resultados parciales y retiros. `/ReporteOperaciones` agrupa por tipo/estado y exporta CSV; usa un importe vigente por operación.
- Tracking: mismo enlace/capability y progreso público por conteos, sin identificadores internos.

Ver el ADR, demostración y evidencia separada por compilación/HTTP/navegador/Android en `docs/cambios-requerimientos` del principal. La navegación de Commerce conserva su catálogo compartido y las acciones conservan autorización en la API.
