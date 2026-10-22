# NexoRuta — reglas persistentes de diseño

Decisión del proyecto: interfaces claras, consistentes y accesibles. Aplicar estas reglas en nuevas pantallas y en los componentes modificados. No iniciar un rediseño general por agregar esta guía.

## Incorporación en el repositorio

Guardar este documento como `frontend/DESIGN.md`. Agregar el siguiente bloque al `AGENTS.md` de la raíz del superproyecto y al del repositorio frontend, conservando sus instrucciones existentes. Las rutas son relativas al repositorio correspondiente.

```markdown
## Diseño de interfaz

Antes de diseñar, implementar o revisar interfaz, leer DESIGN.md del frontend
(frontend/DESIGN.md desde el superproyecto; DESIGN.md desde el frontend).
Aplicar sus cuatro principios: estados de carga con skeletons cuando correspondan;
decisiones simples y jerarquía visual; design tokens semánticos; accesibilidad.
Reutilizar el stack y los componentes existentes. Las referencias y skills externas
complementan estas reglas; no sustituyen las decisiones del proyecto.
Verificar en navegador los cambios visuales y de interacción. Informar por separado
lo comprobado por compilación, HTTP, automatización y revisión visual.
```

`DESIGN.md` es una convención del proyecto: enlazarlo explícitamente desde `AGENTS.md`. Mantener el bloque breve y evitar duplicar esta guía en las instrucciones globales del equipo. Al incorporar nuevas instrucciones, iniciar una sesión nueva de Codex y comprobar qué archivos de instrucciones cargó.

## Contexto técnico y alcance

La revisión remota de `NexoRuta/NexoRuta-Frontend`, rama `develop`, árbol `82ba74686bd5a4f96d3dd17346dddab2349ecbd5`, mostró Backoffice con Razor Pages y Bootstrap; Commerce y Tracking tienen componentes Blazor y estilos propios. Verificar siempre el checkout local antes de implementar: esta observación no fija su versión ni demuestra el estado de la ejecución local.

Preservar estructura por funcionalidades, rutas, permisos, antiforgery y límites entre portales. No introducir React, Tailwind, otra biblioteca de componentes o un nuevo framework para cumplir estas reglas. Reutilizar Bootstrap y los mecanismos actuales cuando resuelvan la necesidad.

## 1. Cargas: skeletons con propósito

- En carga inicial asíncrona de contenido con estructura conocida, mostrar un skeleton que represente los bloques reales: formulario, filas, tarjeta o vista previa. Reservar dimensiones para evitar saltos al aparecer los datos.
- No mostrar skeletons cuando el contenido ya llega completo con la respuesta del servidor. En Razor Pages, evaluar las cargas parciales reales; no agregar JavaScript o una espera artificial solo para mostrar una animación.
- Durante una actualización, conservar el contenido previo cuando siga siendo válido y señalar que se actualiza. No vaciar toda la pantalla por cada cambio.
- Al guardar, mostrar progreso en el botón o región de acción y prevenir el doble envío. No sustituir el formulario por un skeleton ni perder sus valores o el foco.
- Distinguir carga, vacío, error, datos disponibles y falta de permisos. Un error no puede dejar un skeleton indefinido. Ofrecer reintento cuando corresponda.
- Skeletons decorativos fuera del árbol accesible con `aria-hidden="true"`; región afectada con `aria-busy` mientras carga y un mensaje de estado accesible breve cuando sea útil. No anunciar cada bloque ni cada cuadro de animación.
- Respetar `prefers-reduced-motion`: usar una representación estática cuando corresponda. No mover el foco al terminar una carga ordinaria.
- Evitar parpadeos con una demora de aparición breve cuando la medición lo justifique. Nunca retrasar datos listos para prolongar la animación.

**Criterio de aceptación:** con red lenta se entiende qué región carga y dónde aparecerá el contenido; con respuesta rápida no hay destellos molestos; al fallar se abandona el estado de carga.

## 2. Ley de Hick y jerarquía visual

- Diseñar alrededor de la tarea principal. Dar predominio a una acción principal por contexto de tarea; las acciones secundarias deben tener menor peso. Esto es una convención del producto, no un límite matemático de la ley de Hick.
- Reducir decisiones simultáneas, opciones redundantes y caminos equivalentes. Agrupar por tarea y revelar opciones avanzadas cuando hagan falta. No ocultar información necesaria para decidir o corregir un error.
- No inventar un máximo universal de opciones, ni dividir formularios simples en un asistente solo para tener menos campos visibles.
- Usar un título principal claro por página y subtítulos solo para grupos reales. Evitar repetir el título con un encabezado decorativo, un subtítulo genérico y un párrafo que dicen lo mismo.
- Cada texto debe identificar una acción, explicar una consecuencia, aportar contexto necesario o ayudar a resolver un error. Eliminar frases de bienvenida, marketing y explicaciones del desarrollo en pantallas operativas.
- Etiquetas concretas en español: «Guardar cambios», «Cancelar», «Quitar logo». Evitar «Continuar» si se puede nombrar la acción. La concisión no justifica etiquetas ambiguas.
- Ayudas junto al campo y solo cuando aporten formato o significado. No usar tooltips para información imprescindible.
- Crear jerarquía con posición, separación, tamaño y peso antes de sumar colores, bordes o tarjetas. Evitar que todos los bloques compitan por atención.
- Mantener navegación predecible y agrupada. Si crece, revisar su organización dentro del alcance autorizado, conservando permisos y destinos.

**Criterio de aceptación:** se reconoce la tarea, la acción principal y el siguiente paso sin leer párrafos de presentación. Se conserva todo el contexto necesario para operar.

## 3. Design tokens como fuente de verdad

- Antes de crear tokens, inventariar CSS, variables Bootstrap, colores, tipografía y componentes propios. Reutilizar o mapear lo existente. Registrar la ubicación real de la fuente de tokens y cómo llega a cada portal.
- Separar valores de paleta de roles semánticos. Los componentes consumen roles como superficie, texto principal, texto secundario, borde, acción principal, foco, error, éxito y advertencia.
- Definir también escalas de espaciado, tipografía, radios, sombras y movimiento cuando se necesiten. No inventar un sistema enorme para una única pantalla.
- Los siguientes nombres expresan roles propuestos, no tokens que ya existan: `--nr-surface`, `--nr-text`, `--nr-text-muted`, `--nr-border`, `--nr-action-bg`, `--nr-action-text`, `--nr-focus`, `--nr-danger`, `--nr-space-*`.
- Los valores literales de color se centralizan en la definición de tokens o en el límite validado de tematización. Evitar hexadecimales, colores inline y combinaciones independientes por componente. No modificar los archivos vendorizados de Bootstrap.
- Integrar los tokens con las variables y estados reales de Bootstrap: normal, hover, active, focus, disabled e invalid. No basta con cambiar un único color primario.
- Definir un mecanismo explícito de reutilización entre hosts sin acoplar un portal al ensamblado de otro. Si hay copias inevitables, documentar su generación o sincronización; no crear fuentes editables divergentes.
- Registrar cualquier token nuevo con su función. Si un valor no encaja, revisar la escala antes de agregar una excepción.

**Criterio de aceptación:** el mismo rol visual se resuelve desde la misma definición; modificar la marca no exige editar el CSS de cada página.

### Fuente compartida en los portales actuales

La única fuente editable de los tokens semánticos compartidos es `shared/portal-design.css`. `Directory.Build.targets` la copia a `wwwroot/css/portal-design.css` de Backoffice, Commerce y Tracking antes de preparar los assets estáticos; esas copias son generadas e ignoradas por Git. Los tres portales enlazan el mismo nombre de asset. Modificar la fuente compartida, no las copias generadas.

## 4. Accesibilidad: objetivo WCAG 2.2 AA

- Usar HTML semántico: enlaces para navegar, botones para acciones, encabezados en orden, regiones identificables y tablas con encabezados cuando los datos sean tabulares. Preferir controles nativos antes que recrearlos con ARIA.
- Todo campo debe tener una etiqueta visible y asociada mediante `label` y `for`/`id` o un mecanismo nativo equivalente. El placeholder no reemplaza la etiqueta. Agrupar controles relacionados con `fieldset` y `legend` cuando corresponda.
- Identificar campos obligatorios y formatos necesarios. Asociar ayudas y errores con el control; usar `aria-invalid` cuando corresponda. Conservar valores ante error y facilitar encontrar el primer campo inválido.
- Imágenes informativas con texto alternativo útil; decorativas con `alt=""`. Los iconos decorativos se ocultan de tecnologías de asistencia; los botones que solo tienen icono necesitan nombre accesible.
- Texto normal: contraste mínimo 4,5:1. Texto grande: 3:1 (al menos 24 CSS px regular o aproximadamente 18,7 CSS px en negrita). Componentes y señales visuales necesarias para identificarlos: 3:1 respecto de colores adyacentes, según el criterio aplicable.
- No comunicar estados solo mediante color: acompañar con texto, forma o icono con significado accesible. No eliminar indicaciones de error o permisos para simplificar la pantalla.
- Toda operación debe poder realizarse con teclado. Foco visible, orden lógico, sin trampas y sin quedar oculto bajo elementos fijos. En diálogos, gestionar entrada y devolución del foco según el patrón correspondiente.
- Objetivo del proyecto para controles táctiles: 44 × 44 CSS px. Distinguirlo del mínimo AA de WCAG 2.2, que contempla 24 × 24 CSS px y excepciones de espaciado, equivalencia y contexto.
- Comprobar aumento de texto al 200 % y reflujo a 320 CSS px sin pérdida de contenido ni funciones. Evitar desplazamiento horizontal general; las tablas que necesitan dos dimensiones pueden tener una región desplazable identificada.
- No deshabilitar zoom ni pegar contenido. Respetar preferencias de movimiento reducido. Los estados de guardado y error deben ser perceptibles sin depender de animaciones.

Estos puntos son controles prioritarios, no una lista exhaustiva de WCAG. Un escaneo automático sin hallazgos no certifica conformidad de todo el producto.

## Aplicación a CU-05: identidad visual del operador

Antes de implementar, leer la definición vigente de CU-05 y sus dependencias en PDF/XLSM; este documento no reemplaza el alcance funcional. Confirmar campos, permisos, consumidores y comportamiento al guardar. No ejecutar macros.

Si el alcance confirma marca, colores y contacto:

- Organizar la edición en grupos claros —identidad, apariencia y contacto— con una acción de guardado predominante. Usar una vista previa útil con datos ficticios identificados como ejemplo, sin introducir secciones promocionales.
- La vista previa no es una confirmación de persistencia. Distinguir cambios sin guardar, guardado en curso, éxito y error.
- Separar tokens de marca por operador de tokens semánticos de estado. Un color corporativo no debe transformar un error en una señal de éxito ni volver invisible el foco.
- Validar pares reales de fondo/texto, enlaces y estados interactivos. Si una selección no permite una combinación accesible, explicar el problema junto al selector y ofrecer una alternativa válida. Elegir texto claro u oscuro no garantiza que bordes, enlaces y foco también cumplan.
- Definir el comportamiento seguro ante configuración histórica inválida: usar una combinación accesible de respaldo y mostrar el problema a quien puede corregirla. No cambiar silenciosamente el color elegido al guardar.
- Limitar la tematización al operador correcto y evitar destellos de la identidad de otro operador. No usar un estado global compartido que mezcle marcas entre sesiones o tenants.
- Logo con dimensiones reservadas, proporción conservada, texto alternativo contextual y respaldo con nombre cuando falte. Un logo puede estar exento del criterio de contraste de texto; esa excepción no se extiende a botones o etiquetas.
- Mantener la información de contacto legible, con etiquetas y enlaces apropiados. Adaptar email y otros canales a sus capacidades reales: no asumir que todo cliente interpreta CSS o interacciones web.
- Confirmar qué superficies consumen la marca: Commerce, seguimiento y comunicaciones solo cuando el alcance lo exija. No declarar integración si únicamente se cambió la vista previa de Backoffice.

## Navegación entre rutas

- Mantener una única fuente de destinos y reglas de visibilidad para el menú principal de cada portal; Razor Pages y Blazor deben consumir el mismo catálogo cuando comparten navegación.
- Evaluar permisos desde el contexto validado actual de sesión y organización. La visibilidad puede variar por permisos reales; la ruta y el endpoint deben conservar sus propias políticas de autorización.
- Cambiar solo el estado activo y el contenido de página al navegar con el mismo contexto. Cargar o actualizar permisos explícitamente; comunicar carga/error y no convertir un fallo del servicio de permisos en acceso abierto.
- No usar `@onclick` de un shell renderizado estáticamente para un menú colapsable. Preferir controles HTML accesibles con mejora progresiva mediante el JavaScript local existente; sincronizar `aria-expanded`, cerrar al elegir una ruta y permitir Escape con foco devuelto al botón.
- En móvil, la navegación cerrada no debe quedar enfocada ni ocupar espacio; al abrir, los destinos deben seguir disponibles por teclado y tacto. Al cruzar el breakpoint hacia escritorio, normalizar el estado colapsado.

## Flujo de verificación para cambios de UI

1. Leer instrucciones, alcance, tokens y componentes existentes; identificar tarea, estados y superficies afectadas.
2. Proponer la estructura más simple que resuelva la tarea y reutilizar patrones del producto.
3. Implementar todos los estados que apliquen y comprobar contraste de las combinaciones utilizadas.
4. Ejecutar build y pruebas enfocadas correspondientes al cambio. No ejecutar suites completas por rutina ni agregar tests que solo repitan el marcado.
5. Probar la interfaz real en navegador de escritorio y móvil, incluyendo 320 CSS px, teclado, foco y zoom. Simular demora/error para comprobar cargas; verificar que las condiciones se activaron realmente.
6. Usar el escáner de accesibilidad disponible como complemento. Revisar visualmente y con teclado lo que el escáner no prueba. Registrar pendientes concretos si faltan herramientas.
7. Informar archivos, resultados, tamaños comprobados y límites de la evidencia. No presentar peticiones HTTP como revisión visual ni una inspección visual como certificación WCAG.

No ampliar el cambio a otras pantallas sin necesidad. En tareas exclusivamente documentales no ejecutar builds ni pruebas de navegador sin un motivo concreto.

## Skills y fuentes de apoyo

- **Vercel Web Design Guidelines:** guía transversal de interacción y revisión. Su skill `web-design-guidelines` revisa archivos contra reglas que consulta en su fuente; adaptarla al stack real y a esta política. No implica adoptar React/Next.js.
- **Taste Skill:** referencia candidata `Leonxlnx/taste-skill`, pendiente de confirmar si es la que se quiso señalar. Evaluar la skill y su compatibilidad antes de adoptarla. Las decisiones de estilo sugeridas deben respetar el producto y esta guía; no autorizar una dirección estética aleatoria en un sistema existente.
- **awesome-design-md:** colección de referencias de diseño. Consultar patrones concretos; no copiar una identidad de otra marca ni reemplazar las decisiones de NexoRuta con un documento entero.
- **Playwright CLI:** apoyo para navegar, inspeccionar y comprobar interacciones reales. No diseña la interfaz ni sustituye las comprobaciones de accesibilidad. Reutilizar la automatización ya disponible antes de agregar otra instalación.

No es necesario instalar estas herramientas para conservar esta memoria. Su instalación y ejecución deben respetar las instrucciones y el entorno del repositorio.

Fuentes consultadas el 21/10/2026:

- https://learn.chatgpt.com/docs/agent-configuration/agents-md
- https://vercel.com/design/guidelines
- https://github.com/vercel-labs/agent-skills/blob/main/skills/web-design-guidelines/SKILL.md
- https://github.com/Leonxlnx/taste-skill
- https://github.com/voltagent/awesome-design-md
- https://playwright.dev/agent-cli/introduction
- https://playwright.dev/agent-cli/skills
- https://www.w3.org/TR/WCAG22/
