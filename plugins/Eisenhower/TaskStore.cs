using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarkdownVault.Plugin.Eisenhower;

/// <summary>
/// Una tarea de la matriz de Eisenhower. <see cref="Quadrant"/> se calcula a partir
/// de <see cref="Urgent"/> y <see cref="Important"/> — nunca se persiste (no tiene
/// backing field, así que no participa ni en la serialización JSON ni en la
/// igualdad estructural del record).
/// </summary>
public sealed record TaskItem(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("urgent")] bool Urgent,
    [property: JsonPropertyName("important")] bool Important,
    [property: JsonPropertyName("done")] bool Done,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("linkPath")] string? LinkPath = null,
    [property: JsonPropertyName("completedAt")] DateTimeOffset? CompletedAt = null)
{
    /// <summary>Cuadrante derivado de Urgent/Important. Calculado, no serializado.</summary>
    [JsonIgnore]
    public string Quadrant => TaskStore.GetQuadrant(Urgent, Important);
}

/// <summary>Resultado del intento de carga de <c>tasks.json</c> — discriminado por <see cref="LoadStatus"/>.</summary>
public enum LoadStatus
{
    /// <summary>Carga exitosa (puede tener cero tareas).</summary>
    Ok,

    /// <summary>No había contenido (archivo ausente, null o en blanco) — cero tareas, sin error.</summary>
    Empty,

    /// <summary>El contenido no es JSON válido con la forma esperada.</summary>
    Corrupt,

    /// <summary>El campo <c>version</c> del archivo es mayor que <see cref="TaskStore.SchemaVersion"/>.</summary>
    NewerVersion,
}

/// <summary>Resultado inmutable de <see cref="TaskStore.Load"/>.</summary>
public sealed record LoadResult(LoadStatus Status, IReadOnlyList<TaskItem> Tasks);

/// <summary>Resultado de un intento de captura — discriminado por <see cref="CaptureStatus"/>.</summary>
public enum CaptureStatus
{
    /// <summary>Captura exitosa: <c>Task</c> y <c>Json</c> están presentes.</summary>
    Ok,

    /// <summary>El título está vacío o solo contiene espacios — no se agregó ninguna tarea.</summary>
    BlankTitle,

    /// <summary>El archivo actual es <see cref="LoadStatus.Corrupt"/> o <see cref="LoadStatus.NewerVersion"/> —
    /// se abortó la captura para no sobrescribir datos ilegibles.</summary>
    Unreadable,
}

/// <summary>
/// Resultado inmutable de <see cref="TaskStore.Capture"/>. En <see cref="CaptureStatus.Ok"/>,
/// <c>Task</c> es la tarea nueva y <c>Json</c> es la lista completa (existentes + nueva) ya
/// serializada, lista para escribirse. En cualquier otro estado ambos son <c>null</c> — no hay
/// payload de escritura.
/// </summary>
public sealed record CaptureResult(CaptureStatus Status, TaskItem? Task, string? Json);

/// <summary>
/// Resultado inmutable de <see cref="TaskStore.Add"/>. En <c>Ok == true</c>, <c>Task</c>
/// es la tarea nueva y <c>Json</c> es la lista completa ya serializada. En
/// <c>Ok == false</c> (título en blanco) ambos son <c>null</c>.
/// </summary>
public sealed record AddResult(bool Ok, IReadOnlyList<TaskItem> Tasks, TaskItem? Task, string? Json);

/// <summary>
/// Resultado inmutable de una mutación de lista en memoria (<see cref="TaskStore.ToggleDone"/>,
/// <see cref="TaskStore.Remove"/>): la lista resultante y su serialización, listas para que
/// el llamador (ventana Eisenhower) persista vía <c>IPluginStorage</c>.
/// </summary>
public sealed record TaskListResult(IReadOnlyList<TaskItem> Tasks, string Json);

/// <summary>
/// Un grupo mensual de tareas completadas — la unidad que alimenta cada pestaña de la
/// sección "Completadas" (ver <see cref="TaskStore.CompletedByMonth"/>). <c>Year</c>/<c>Month</c>
/// identifican el grupo (útiles para ordenar/comparar sin parsear <c>Label</c>); <c>Label</c> es
/// el título en español ya formateado para el header del <c>TabItem</c>.
/// </summary>
public sealed record MonthGroup(int Year, int Month, string Label, IReadOnlyList<TaskItem> Tasks);

/// <summary>
/// Lógica pura de datos del plugin Eisenhower: modelo, JSON I/O y el cálculo de
/// cuadrante. Sin WPF, sin I/O de disco — el llamador (render/captura) resuelve el
/// texto crudo y se lo pasa a <see cref="Load"/>.
/// </summary>
public static class TaskStore
{
    /// <summary>
    /// Versión actual del esquema de <c>tasks.json</c>. v2 agrega <see cref="TaskItem.LinkPath"/>
    /// (opcional/nullable) — un archivo v1 sin ese campo sigue cargando <see cref="LoadStatus.Ok"/>
    /// (1 &lt;= 3) con <c>LinkPath == null</c>. v3 agrega <see cref="TaskItem.CompletedAt"/>
    /// (opcional/nullable) — un archivo v2 sin ese campo sigue cargando <see cref="LoadStatus.Ok"/>
    /// (2 &lt;= 3) con <c>CompletedAt == null</c> (ver fallback a <c>UpdatedAt</c> en
    /// <see cref="CompletedByMonth"/>). Ninguna de las dos requiere migración explícita.
    /// </summary>
    public const int SchemaVersion = 3;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    private sealed class TaskFile
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("tasks")]
        public List<TaskItem> Tasks { get; set; } = new();
    }

    /// <summary>Mapea (urgent, important) al nombre del cuadrante de Eisenhower.</summary>
    public static string GetQuadrant(bool urgent, bool important) =>
        (urgent, important) switch
        {
            (true, true) => "Hacer ahora",
            (false, true) => "Planificar",
            (true, false) => "Delegar",
            (false, false) => "Eliminar",
        };

    /// <summary>
    /// Parsea el contenido crudo de <c>tasks.json</c>. Nunca lanza: cualquier fallo
    /// se traduce a un <see cref="LoadStatus"/> explícito.
    /// </summary>
    public static LoadResult Load(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new LoadResult(LoadStatus.Empty, Array.Empty<TaskItem>());
        }

        TaskFile? file;
        try
        {
            file = JsonSerializer.Deserialize<TaskFile>(raw, JsonOpts);
        }
        catch (JsonException)
        {
            return new LoadResult(LoadStatus.Corrupt, Array.Empty<TaskItem>());
        }

        if (file is null)
        {
            return new LoadResult(LoadStatus.Corrupt, Array.Empty<TaskItem>());
        }

        if (file.Version > SchemaVersion)
        {
            return new LoadResult(LoadStatus.NewerVersion, Array.Empty<TaskItem>());
        }

        return new LoadResult(LoadStatus.Ok, file.Tasks);
    }

    /// <summary>Serializa la lista de tareas al formato de <c>tasks.json</c> (envuelto con <c>version</c>).</summary>
    public static string Serialize(IReadOnlyList<TaskItem> tasks)
    {
        var file = new TaskFile { Version = SchemaVersion, Tasks = tasks.ToList() };
        return JsonSerializer.Serialize(file, JsonOpts);
    }

    /// <summary>
    /// Construye una <see cref="TaskItem"/> nueva a partir de los campos del formulario de
    /// captura y la agrega a la lista cargada en <paramref name="current"/>. Pura: no toca
    /// disco — el llamador es responsable de persistir <see cref="CaptureResult.Json"/> vía
    /// <c>IPluginStorage</c>.
    /// </summary>
    /// <remarks>
    /// Nunca sobrescribe un archivo ilegible: si <paramref name="current"/> viene de un
    /// <see cref="LoadStatus.Corrupt"/> o <see cref="LoadStatus.NewerVersion"/>, aborta con
    /// <see cref="CaptureStatus.Unreadable"/> sin construir ni serializar nada (datos
    /// sensibles a pérdida — preferimos bloquear la captura antes que perder tareas
    /// existentes ilegibles).
    /// </remarks>
    public static CaptureResult Capture(LoadResult current, string? title, bool urgent, bool important)
    {
        if (current.Status is LoadStatus.Corrupt or LoadStatus.NewerVersion)
        {
            return new CaptureResult(CaptureStatus.Unreadable, null, null);
        }

        var added = Add(current.Tasks, title, urgent, important);
        if (!added.Ok)
        {
            return new CaptureResult(CaptureStatus.BlankTitle, null, null);
        }

        return new CaptureResult(CaptureStatus.Ok, added.Task, added.Json);
    }

    /// <summary>
    /// Construye una <see cref="TaskItem"/> nueva a partir de título/urgente/importante y la
    /// agrega a <paramref name="tasks"/>. Bloque compartido detrás de <see cref="Capture"/>
    /// (que además valida el <see cref="LoadStatus"/> del archivo) y de la futura ventana
    /// Eisenhower, que ya trabaja sobre una lista en memoria válida y no necesita ese chequeo.
    /// Pura: no toca disco — el llamador persiste <see cref="AddResult.Json"/>.
    /// </summary>
    public static AddResult Add(IReadOnlyList<TaskItem> tasks, string? title, bool urgent, bool important)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return new AddResult(false, tasks, null, null);
        }

        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), title, urgent, important, false, now, now);

        var updated = new List<TaskItem>(tasks) { task };
        var json = Serialize(updated);

        return new AddResult(true, updated, task, json);
    }

    /// <summary>
    /// Invierte el flag <c>Done</c> de la tarea con <paramref name="id"/> y actualiza su
    /// <c>UpdatedAt</c>. Al pasar a <c>Done == true</c> también fija <c>CompletedAt</c> (marca
    /// de tiempo de la finalización, usada por <see cref="CompletedByMonth"/> para agrupar el
    /// historial); al volver a <c>Done == false</c>, lo limpia (<c>null</c>) — la tarea deja de
    /// estar completada, no debería seguir cargando una fecha de finalización vieja. Id
    /// desconocido → lista sin cambios (no-op, no lanza). Pura: no muta <paramref name="tasks"/>
    /// in-place — devuelve una lista nueva.
    /// </summary>
    public static TaskListResult ToggleDone(IReadOnlyList<TaskItem> tasks, Guid id)
    {
        var index = IndexOf(tasks, id);
        if (index < 0)
        {
            return new TaskListResult(tasks, Serialize(tasks));
        }

        var updated = new List<TaskItem>(tasks);
        var target = updated[index];
        var nowDone = !target.Done;
        updated[index] = target with
        {
            Done = nowDone,
            UpdatedAt = DateTimeOffset.UtcNow,
            CompletedAt = nowDone ? DateTimeOffset.UtcNow : null,
        };

        return new TaskListResult(updated, Serialize(updated));
    }

    /// <summary>
    /// Actualiza los flags <c>Urgent</c>/<c>Important</c> de la tarea con <paramref name="id"/>
    /// (lo que recalcula su <see cref="TaskItem.Quadrant"/> — cuadrante derivado, no
    /// persistido) y actualiza su <c>UpdatedAt</c>. Id desconocido → lista sin cambios (no-op,
    /// no lanza). Pura: no muta <paramref name="tasks"/> in-place — devuelve una lista nueva,
    /// mismo patrón que <see cref="ToggleDone"/>.
    /// </summary>
    public static TaskListResult SetClassification(IReadOnlyList<TaskItem> tasks, Guid id, bool urgent, bool important)
    {
        var index = IndexOf(tasks, id);
        if (index < 0)
        {
            return new TaskListResult(tasks, Serialize(tasks));
        }

        var updated = new List<TaskItem>(tasks);
        var target = updated[index];
        updated[index] = target with { Urgent = urgent, Important = important, UpdatedAt = DateTimeOffset.UtcNow };

        return new TaskListResult(updated, Serialize(updated));
    }

    /// <summary>
    /// Actualiza el <c>Title</c> de la tarea con <paramref name="id"/> y su <c>UpdatedAt</c>.
    /// Título en blanco (null/espacios) → no-op, la tarea conserva su título anterior (mismo
    /// criterio de rechazo que <see cref="Add"/>). Id desconocido → lista sin cambios (no-op,
    /// no lanza). Pura: no muta <paramref name="tasks"/> in-place — devuelve una lista nueva,
    /// mismo patrón que <see cref="SetClassification"/>.
    /// </summary>
    public static TaskListResult SetTitle(IReadOnlyList<TaskItem> tasks, Guid id, string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return new TaskListResult(tasks, Serialize(tasks));
        }

        var index = IndexOf(tasks, id);
        if (index < 0)
        {
            return new TaskListResult(tasks, Serialize(tasks));
        }

        var updated = new List<TaskItem>(tasks);
        var target = updated[index];
        updated[index] = target with { Title = title, UpdatedAt = DateTimeOffset.UtcNow };

        return new TaskListResult(updated, Serialize(updated));
    }

    /// <summary>
    /// Establece (o limpia) el <c>LinkPath</c> de la tarea con <paramref name="id"/> y
    /// actualiza su <c>UpdatedAt</c>. <paramref name="relativePath"/> nulo, vacío o solo
    /// espacios limpia el enlace (<c>LinkPath = null</c>) — a diferencia de
    /// <see cref="SetTitle"/>, un valor en blanco es una operación válida (desvincular), no
    /// un rechazo. Id desconocido → lista sin cambios (no-op, no lanza). Pura: no muta
    /// <paramref name="tasks"/> in-place — devuelve una lista nueva, mismo patrón que
    /// <see cref="SetTitle"/>/<see cref="SetClassification"/>.
    /// </summary>
    public static TaskListResult SetLink(IReadOnlyList<TaskItem> tasks, Guid id, string? relativePath)
    {
        var index = IndexOf(tasks, id);
        if (index < 0)
        {
            return new TaskListResult(tasks, Serialize(tasks));
        }

        var normalized = string.IsNullOrWhiteSpace(relativePath) ? null : relativePath;

        var updated = new List<TaskItem>(tasks);
        var target = updated[index];
        updated[index] = target with { LinkPath = normalized, UpdatedAt = DateTimeOffset.UtcNow };

        return new TaskListResult(updated, Serialize(updated));
    }

    /// <summary>
    /// Devuelve una lista sin la tarea con <paramref name="id"/>. Id desconocido → lista sin
    /// cambios (no-op, no lanza). Pura: no muta <paramref name="tasks"/> in-place.
    /// </summary>
    public static TaskListResult Remove(IReadOnlyList<TaskItem> tasks, Guid id)
    {
        var index = IndexOf(tasks, id);
        if (index < 0)
        {
            return new TaskListResult(tasks, Serialize(tasks));
        }

        var updated = new List<TaskItem>(tasks);
        updated.RemoveAt(index);

        return new TaskListResult(updated, Serialize(updated));
    }

    /// <summary>
    /// Tareas pendientes (<c>!Done</c>), en el mismo orden relativo en que aparecen en
    /// <paramref name="tasks"/>. Es lo que la grilla 2x2 de la ventana Eisenhower debe
    /// mostrar en sus cuadrantes — las tareas completadas se mueven a la sección
    /// "Completadas" (ver <see cref="Completed"/>) y dejan de ocupar su cuadrante. Pura:
    /// no muta <paramref name="tasks"/>.
    /// </summary>
    public static IReadOnlyList<TaskItem> Active(IReadOnlyList<TaskItem> tasks) =>
        tasks.Where(t => !t.Done).ToList();

    /// <summary>
    /// Tareas completadas (<c>Done</c>), ordenadas por <c>UpdatedAt</c> descendente (la
    /// completada más recientemente primero). Alimenta la sección de historial
    /// "Completadas" bajo la grilla 2x2. Pura: no muta <paramref name="tasks"/>.
    /// </summary>
    public static IReadOnlyList<TaskItem> Completed(IReadOnlyList<TaskItem> tasks) =>
        tasks.Where(t => t.Done).OrderByDescending(t => t.UpdatedAt).ToList();

    /// <summary>Nombres de mes en español, índice 0 = enero. Fijo — NO depende de <c>CultureInfo</c>
    /// (la UI del SO puede estar en otro idioma; el historial siempre se etiqueta en español).</summary>
    private static readonly string[] SpanishMonths =
    {
        "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre",
    };

    /// <summary>
    /// Agrupa las tareas completadas (<see cref="TaskItem.Done"/>) de <paramref name="tasks"/>
    /// por año+mes de finalización, para alimentar las pestañas mensuales de la sección
    /// "Completadas" (<see cref="EisenhowerWindow"/>). La fecha de finalización es
    /// <see cref="TaskItem.CompletedAt"/> — o, si es <c>null</c> (tareas completadas antes de
    /// que existiera ese campo, schema v2 o anterior), <see cref="TaskItem.UpdatedAt"/> como
    /// fallback. Grupos ordenados por mes más reciente primero; dentro de cada grupo, tareas
    /// ordenadas por esa misma fecha descendente (la completada más recientemente primero,
    /// mismo criterio que <see cref="Completed"/>). Tareas activas (<c>!Done</c>) se excluyen.
    /// Pura: no muta <paramref name="tasks"/>. Sin tareas completadas → lista vacía.
    /// </summary>
    public static IReadOnlyList<MonthGroup> CompletedByMonth(IReadOnlyList<TaskItem> tasks)
    {
        return tasks
            .Where(t => t.Done)
            .Select(t => (Task: t, Date: t.CompletedAt ?? t.UpdatedAt))
            .GroupBy(x => new { x.Date.Year, x.Date.Month })
            .OrderByDescending(g => g.Key.Year)
            .ThenByDescending(g => g.Key.Month)
            .Select(g =>
            {
                var orderedTasks = g.OrderByDescending(x => x.Date).Select(x => x.Task).ToList();
                var label = Capitalize(SpanishMonths[g.Key.Month - 1]) + " " + g.Key.Year;
                return new MonthGroup(g.Key.Year, g.Key.Month, label, orderedTasks);
            })
            .ToList();
    }

    private static string Capitalize(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

    private static int IndexOf(IReadOnlyList<TaskItem> tasks, Guid id)
    {
        for (var i = 0; i < tasks.Count; i++)
        {
            if (tasks[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Definición de un cuadrante para el render: (urgent, important, slug CSS).</summary>
    private static readonly (bool Urgent, bool Important, string Slug)[] Quadrants =
    {
        (true,  true,  "do"),
        (false, true,  "plan"),
        (true,  false, "delegate"),
        (false, false, "eliminate"),
    };

    /// <summary>
    /// Renderiza el resultado de <see cref="Load"/> como el HTML de la grilla de Eisenhower
    /// (o un banner de error explícito para Corrupt/NewerVersion). Pura: sin I/O, sin WPF.
    /// Nunca lanza — cualquier <see cref="LoadStatus"/> tiene una salida HTML definida.
    /// Los títulos de las tareas se escapan (HTML-encode) para evitar inyección.
    /// </summary>
    public static string RenderGridHtml(LoadResult result)
    {
        if (result.Status is LoadStatus.Corrupt or LoadStatus.NewerVersion)
        {
            return RenderErrorBanner(result.Status);
        }

        // Ok o Empty comparten la misma grilla de 4 cuadrantes: Empty simplemente
        // no tiene tareas que ubicar (Load ya garantiza Tasks vacío en ese caso).
        return RenderQuadrantGrid(result.Tasks);
    }

    private static string RenderQuadrantGrid(IReadOnlyList<TaskItem> tasks)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"eisenhower-grid\">");

        foreach (var (urgent, important, slug) in Quadrants)
        {
            var label = GetQuadrant(urgent, important);
            var encodedLabel = WebUtility.HtmlEncode(label);

            sb.Append("<div class=\"eisenhower-quadrant eisenhower-quadrant--")
              .Append(slug)
              .Append("\" data-quadrant=\"")
              .Append(encodedLabel)
              .Append("\">");
            sb.Append("<h3 class=\"eisenhower-quadrant__title\">").Append(encodedLabel).Append("</h3>");
            sb.Append("<ul class=\"eisenhower-quadrant__tasks\">");

            foreach (var task in tasks.Where(t => t.Urgent == urgent && t.Important == important))
            {
                sb.Append("<li class=\"eisenhower-task\">")
                  .Append(WebUtility.HtmlEncode(task.Title))
                  .Append("</li>");
            }

            sb.Append("</ul></div>");
        }

        sb.Append("</div>");
        return sb.ToString();
    }

    private static string RenderErrorBanner(LoadStatus status)
    {
        var message = status == LoadStatus.NewerVersion
            ? "tasks.json fue creado por una versión más nueva de este plugin y no se puede mostrar aquí."
            : "tasks.json no se pudo leer (formato inválido).";

        return "<div class=\"eisenhower-error\" data-status=\"" + status + "\"><p>"
            + WebUtility.HtmlEncode(message) + "</p></div>";
    }
}
