# Lector de Documentos — Guía de usuario

Lee el documento abierto en voz alta. Sirve para revisar un texto largo mientras hacés otra
cosa, para detectar frases mal armadas que a la vista se te escapan, o simplemente para
descansar los ojos.

Usa **Piper**, un sintetizador de voz que corre en tu propia máquina: el texto **nunca sale de
tu computadora**. No necesita internet, ni cuenta, ni clave de ningún servicio. Podés leer el
documento más confidencial que tengas sin pensarlo dos veces.

---

## Cómo se usa

En la barra de herramientas aparece el menú **Lector**:

| Opción | Qué hace |
| ------ | -------- |
| **▶ Leer documento** | Lee el documento completo desde el principio. |
| **▶ Leer selección** | Lee solo el texto que tengas seleccionado en el editor. |
| **⏸ Pausar / Reanudar** | Alterna: la primera vez pausa, la siguiente sigue donde quedó. |
| **⏹ Detener** | Corta la lectura. Volver a leer arranca desde el principio. |
| **Velocidad: lenta / normal / rápida / muy rápida** | Se aplica en la **próxima** lectura, no en la que está sonando. |
| **Voz: …** | Una entrada por cada voz instalada. También se aplica en la próxima lectura. |
| **ⓘ Estado del lector** | Muestra en la barra de estado qué voz y velocidad están activas, cuántas pronunciaciones hay cargadas y dónde encontró Piper. |

El punto (`•`) al costado de una velocidad o una voz marca cuál estaba elegida **al arrancar la
app**. Si la cambiás durante la sesión, la confirmación aparece en la barra de estado — la marca
del menú no se mueve hasta el próximo arranque.

La barra de estado también va mostrando el avance: `Lector: leyendo 7 de 41…`.

---

## Qué lee y qué se saltea

El lector no pronuncia el Markdown: lo traduce a lo que diría una persona leyendo el documento
en voz alta.

**Se lee:**

- Los títulos, como una frase aparte (con su pausa natural).
- Los párrafos, las citas y los ítems de las listas, sin los guiones ni la numeración.
- Las tablas, fila por fila, separando las celdas con comas.
- El texto de los enlaces — el texto, no la dirección.

**Se saltea:**

- Los bloques de código completos (` ``` `). Escuchar código dictado no le sirve a nadie.
- Las imágenes y las direcciones web sueltas. Una URL deletreada es una tortura.
- Los comentarios HTML y el *front matter* de YAML.
- Los asteriscos, almohadillas y demás marcas de formato.

Si el documento son puras imágenes y código, el lector avisa que no hay texto legible en vez de
quedarse mudo.

---

## Palabras en inglés y términos técnicos

Si leés documentación técnica en español, el texto está lleno de palabras inglesas. Sin ayuda,
la voz decía *"pipeliné"* y, peor, leía `C#` como **"C almohadilla"**.

**Por qué pasaba**: el modelo de voz SÍ tiene los sonidos ingleses en su inventario — la `sh` de
*shop*, las dos `th` de *think* y *this*, la `æ` de *cat*, la `r` inglesa. Lo que falla es el paso
anterior: el fonemizador está fijado en español y le aplica reglas de lectura españolas a todo.

**Cómo se arregla**: escribiéndole la palabra **como se lee en español**. "páiplain" pasa por las
reglas españolas y sale sonando a *pipeline*. Eso hace el diccionario de pronunciación, que se
aplica justo antes de mandarle el texto a la voz.

### Es tuyo: editálo

Vive acá, y la primera vez que usás el lector se crea solo con unos 36 términos de arranque:

```
%AppData%\MarkdownVault\PluginData\core.lector-documentos\pronunciaciones.json
```

```json
{
  "C#": "ci sharp",
  ".NET": "punto net",
  "pipeline": "páiplain",
  "framework": "fréimwork",
  "Python": "paitón"
}
```

Izquierda lo que dice el documento, derecha cómo querés que suene. Reglas:

- **No distingue mayúsculas**: una sola entrada cubre `pipeline`, `Pipeline` y `PIPELINE`.
- **Reemplaza palabras enteras**: `pipeline` no toca `pipelineado`. Por eso los **plurales
  necesitan su propia entrada** (`pipelines` ya viene incluido).
- **Gana el término más largo**: `MarkdownVault` se resuelve antes que `Markdown`.
- **Una sola pasada**: lo que sale de un reemplazo no se vuelve a reemplazar.
- Si rompés el JSON, el lector avisa y sigue con los valores de fábrica — nunca se queda mudo.

La pronunciación es cuestión de oído, así que los valores que vienen son un punto de partida,
no una verdad. Probá, corregí la grafía hasta que suene bien, y guardá. Los cambios entran al
**desactivar y reactivar** el complemento.

---

## Por qué empieza a hablar enseguida

Sintetizar un documento largo entero llevaría decenas de segundos de silencio antes de la
primera palabra. En vez de eso, el lector parte el texto en fragmentos cortos y va **generando
el siguiente mientras suena el actual**. Por eso arranca en un par de segundos, sin importar el
largo del documento.

Los números medidos con la voz que trae: generar audio cuesta **0,8 s fijos** de arranque del
proceso más **0,0095 s por carácter**. Como reproducir es 4 veces más lento que generar, la
cola nunca se queda sin audio.

Por eso el **primer** fragmento se parte más corto que los demás (120 caracteres en vez de 320):
es el único que escuchás en silencio. Baja la espera inicial de 3,8 s a 1,5 s. Del segundo en
adelante ya no importa, porque se genera mientras suena el anterior.

Consecuencia práctica: si cambiás la velocidad o la voz en mitad de una lectura, el cambio entra
en la próxima, porque los fragmentos que vienen ya están generados o en camino.

---

## Voces

El complemento viene con **es_AR-daniela-high** (español de Argentina, calidad alta), elegida
por comparación a oído contra es_MX-laura-high, es_ES-davefx-medium y es_MX-ald-medium.

Para agregar otras, bajá el par de archivos de la voz (`.onnx` y `.onnx.json`) desde el catálogo
de voces de Piper y copialos a:

```
Plugins/LectorDocumentos/runtime/models/
```

(dentro de la carpeta donde está `MarkdownVault.exe`). Después **desactivá y volvé a activar** el
complemento desde el menú Complementos: las voces se enumeran al activarse, no en caliente.

Ojo con un malentendido frecuente: el escalón `x_low` / `low` / `medium` / `high` es el **tamaño
del modelo y su frecuencia de muestreo**, NO la calidad de la actuación. Lo que manda es el
dataset con el que se grabó la voz. Una `medium` bien grabada le gana a una `high` mediocre.

El catálogo oficial en español (ver `VOICES.md` de Piper) es: **es_ES** carlfm, davefx,
mls_10246, mls_9972, sharvard · **es_MX** ald, claude · **es_AR** daniela.

---

## Si no encuentra Piper

Elegí **ⓘ Estado del lector**: la barra de estado te va a decir en qué rutas buscó. El orden es:

1. La carpeta que hayas configurado a mano (ver abajo).
2. `Plugins/LectorDocumentos/runtime/` — la que viene con el complemento. **Esta es la normal.**
3. `C:\piper`, por si tenés una instalación suelta.
4. Las carpetas del `PATH`.

Para apuntar a otra instalación, editá:

```
%AppData%\MarkdownVault\PluginData\core.lector-documentos\config.json
```

```json
{
  "piperDir": "D:\\herramientas\\piper",
  "voice": "es_AR-daniela-high",
  "lengthScale": 1.0
}
```

`lengthScale` es la duración de los sonidos: **mayor a 1 es más lento**, menor a 1 más rápido.
Ese archivo es también donde el complemento guarda tu voz y tu velocidad; vive en su propia
carpeta aislada y nunca toca tu vault.

---

## Detalles que conviene saber

- **Se puede prender y apagar en caliente**, sin reiniciar la app: a diferencia de Eisenhower,
  este complemento no abre ventanas propias. Al apagarlo, corta el audio y mata cualquier
  proceso de síntesis en curso.
- **Piper corre como un proceso aparte**, no dentro de la app. Es a propósito: si el motor de
  voz falla, se lleva puesto un proceso descartable y no MarkdownVault.
- Los audios temporales van a `%TEMP%\MarkdownVault.Lector\` y se borran solos a medida que se
  reproducen. Si la app se cierra de golpe en mitad de una lectura, los restos se limpian en la
  lectura siguiente.
- **Pesa ~137 MB** en disco: el motor y las reglas de pronunciación son 27 MB; el resto (109 MB)
  es la voz.
- Si buscás naturalidad de otro nivel, el límite no es la voz sino Piper: es un modelo pensado
  para correr en una Raspberry Pi. El salto real sería cambiar de motor (Kokoro, o las voces
  Naturales de Windows 11). El código está preparado: `PiperEngine` es la única pieza atada a
  Piper; el filtro de Markdown y la reproducción se reusan tal cual.
