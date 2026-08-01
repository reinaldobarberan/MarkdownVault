# Eisenhower — Guía de usuario

El plugin **Eisenhower** es un gestor de tareas personal basado en la matriz de Eisenhower (urgente × importante), integrado en MarkdownVault como una ventana dedicada. Este documento explica cómo usarlo. Para la referencia técnica del sistema de plugins en general, ver [`PLUGINS.md`](PLUGINS.md) y, para desarrolladores, [`GUIA-PLUGINS.md`](GUIA-PLUGINS.md).

## El concepto: la matriz de Eisenhower

Cada tarea se clasifica con dos preguntas independientes — **¿es urgente?** y **¿es importante?** — y esa combinación determina en qué cuadrante cae:

| Urgente | Importante | Cuadrante | Qué significa |
| :-: | :-: | --- | --- |
| Sí | Sí | **Hacer ahora** | Requiere atención inmediata. |
| No | Sí | **Planificar** | Importa, pero no corre — agendala. |
| Sí | No | **Delegar** | Corre, pero no aporta valor propio — pasala a otra persona si podés. |
| No | No | **Eliminar** | Ni urgente ni importante — candidata a descartar. |

Estos cuatro cuadrantes son la grilla 2×2 que ves al abrir la ventana.

## Abrir la ventana

En la barra de herramientas del editor hay un botón con un ícono de grilla (cuatro cuadrados 2×2) y tooltip **"Tareas Eisenhower"**. Al hacer clic se abre la ventana de gestión de tareas.

## La ventana: gestión de tareas

### Agregar una tarea

En la parte superior hay un campo de título y dos casillas, **¿Urgente?** y **¿Importante?**. Completá el título, marcá las casillas que correspondan y confirmá con **Agregar**. La tarea nueva aparece de inmediato en el cuadrante que le toca según esas dos casillas.

### La grilla y su leyenda

Las tareas activas (no completadas) se muestran en una grilla de 2×2, una celda por cuadrante, cada una con su leyenda (**Hacer ahora**, **Planificar**, **Delegar**, **Eliminar**). Una tarea aparece en un único cuadrante a la vez, determinado por sus casillas Urgente/Importante actuales.

### Editar el texto de una tarea

Doble clic sobre el título, o el botón **✎**, lo convierte en un campo editable. Confirmá con Enter o simplemente hacé clic afuera (perder el foco también confirma). Un título en blanco se rechaza — la tarea conserva el título anterior.

### Reclasificar (cambiar de cuadrante)

Cada fila de tarea tiene sus propios interruptores **Urgente** / **Importante**, siempre visibles. Tocar cualquiera de los dos reclasifica la tarea en el acto: se mueve al cuadrante que corresponda a la nueva combinación. No hace falta borrar y volver a crear la tarea para cambiarla de lugar.

### Marcar una tarea como hecha

Cada fila tiene una casilla de completado. Al tildarla, la tarea sale de la grilla 2×2 y pasa a la sección **Completadas** (ver abajo), con su título tachado.

### Enlazar un documento del vault

El botón **🔗** de cada fila vincula la tarea con una nota del vault:

- **Sin enlace todavía**: al hacer clic se abre un diálogo para elegir un archivo, restringido a la carpeta del vault (no podés enlazar algo fuera de él).
- **Ya tiene enlace**: hacer clic sobre el botón **abre esa nota** directamente en el editor.
- **Clic derecho** sobre el botón, con o sin enlace previo, despliega un menú con **"Cambiar enlace…"** (elegir otro archivo) y **"Quitar enlace"** (desvincular sin borrar la tarea).

### Borrar una tarea

El botón **✕** de cada fila la elimina de forma permanente (no pasa por la sección Completadas).

## Sección Completadas

Debajo de la grilla 2×2, la sección **Completadas** agrupa el historial de tareas terminadas en **pestañas por mes** (por ejemplo, "Agosto 2026", "Julio 2026"), con la pestaña del mes más reciente seleccionada por defecto. Dentro de cada pestaña, las tareas se listan de la más reciente a la más antigua.

Cada fila del historial tiene:

- **Restaurar**: destildar la casilla de completado devuelve la tarea a la grilla activa, en el cuadrante que le corresponda según sus casillas Urgente/Importante.
- **Borrar (✕)**: la elimina definitivamente del historial.

## Vista de solo lectura dentro de una nota (opcional)

Además de la ventana, podés incrustar una vista de **solo lectura** de la grilla actual en cualquier nota, con un bloque de código:

````markdown
```eisenhower
```
````

Ese bloque se reemplaza en la vista previa por la misma grilla de 4 cuadrantes (solo título y cuadrante de cada tarea activa — sin enlaces, sin controles de edición). Es un espejo de lectura, no un formulario: para agregar, editar o completar tareas siempre usás la ventana.

## Dónde viven los datos

Las tareas se guardan en `tasks.json`, dentro del sandbox privado del plugin:

```
%AppData%/MarkdownVault/PluginData/core.eisenhower/tasks.json
```

Ese archivo **no** vive en tu vault de notas — es almacenamiento propio del plugin, aislado del de cualquier otro complemento y del contenido de tus notas.

## Limitación conocida (v1)

Eisenhower define su propia ventana (WPF). Por una restricción de la plataforma .NET/WPF, un plugin que trae su propia ventana **no se descarga en caliente** por completo: desactivarlo desde el menú Complementos sí quita al instante su comando de la barra y su contribución a la grilla, pero el ensamblado sigue en memoria hasta que **reiniciás la aplicación**. Es una limitación aceptada para esta versión, documentada en [`GUIA-PLUGINS.md`](GUIA-PLUGINS.md); no afecta al uso normal del plugin, solo al caso de desactivarlo y esperar que libere memoria sin reiniciar.
