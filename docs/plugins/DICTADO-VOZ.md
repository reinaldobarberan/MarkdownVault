# Dictado y Transcripción de Voz — Guía de usuario

Convierte voz en texto dentro del editor, de dos formas: transcribiendo un archivo de audio que ya
tenés (una nota de voz de WhatsApp, una grabación) o dictando en vivo por micrófono, frase por
frase, a medida que hablás. Las dos entradas comparten el mismo motor de reconocimiento.

Usa **whisper.cpp**, un motor de reconocimiento de voz que corre en tu propia máquina: el audio
**nunca sale de tu computadora**. No necesita internet para transcribir (sí la primera vez, para
bajar el modelo — ver abajo), ni cuenta, ni clave de ningún servicio.

---

## Cómo se usa

En la barra de herramientas aparece el menú **Dictado**:

| Opción | Qué hace |
| ------ | -------- |
| **🎙 Transcribir audio…** | Abre un diálogo para elegir un archivo de audio y lo transcribe completo. |
| **⏹ Cancelar transcripción** | Corta la transcripción de archivo en curso. |
| **🎤 Iniciar dictado** | Empieza a escuchar el micrófono: cada pausa inserta lo que dijiste. |
| **⏹ Detener dictado** | Termina la sesión de dictado en vivo y libera el micrófono. |
| **ⓘ Estado del dictado** | Muestra en la barra de estado: motor, modelo, idioma, términos de glosario, y si hay una transcripción o un dictado en curso. |

Transcribir un archivo y dictar por micrófono son cosas **independientes**: el motor atiende los dos
pedidos a la vez si hace falta, así que arrancar una transcripción no corta un dictado en curso, ni
al revés. Lo único que se reemplaza solo es una transcripción de archivo nueva sobre una anterior
(gana la última). El dictado, en cambio, **no** se reemplaza solo: si ya hay uno en curso y volvés a
pedir «Iniciar dictado», el complemento avisa y no hace nada — reabrir el micrófono a mitad de una
sesión te cortaría la frase que estás diciendo en ese momento.

Mientras dura cualquiera de las dos operaciones aparece una **barra de progreso** en la parte
inferior de la ventana, con un botón para cancelar.

---

## La primera vez que lo usás

El complemento pesa dos cosas muy distintas:

- El **motor** (whisper-server.exe + ffmpeg.exe + las bibliotecas de CUDA, ~670 MB) viene
  **empaquetado** con el complemento — no hay que bajar nada para eso.
- El **modelo de reconocimiento** (`ggml-large-v3-turbo-q5_0.bin`, **~574 MB**) se descarga la
  primera vez que activás el complemento o transcribís algo, con una barra de progreso que muestra
  el porcentaje y el tamaño total.

Queda guardado en:

```
%AppData%\MarkdownVault\PluginData\core.dictado-voz\models\ggml-large-v3-turbo-q5_0.bin
```

**Si la descarga se corta** (se fue la conexión, cerraste la app), no se pierde: el archivo parcial
(`.bin.part`) queda ahí y la próxima vez **retoma** desde donde quedó, no arranca de cero. Al
terminar se verifica con un checksum (SHA-256); si por algún motivo no coincide, se descarta y se
reintenta una vez antes de avisar del fallo.

Una vez descargado, activar el complemento arranca el motor en segundo plano — no hace falta
esperar a la primera transcripción para pagar ese costo.

---

## Qué formatos acepta

`.opus` · `.mp3` · `.m4a` · `.mp4` · `.wav`

El primero es el que importa en la práctica: es el formato de las **notas de voz de WhatsApp**. La
conversión al formato que necesita el motor (WAV 16 kHz mono) la hace el propio `whisper-server.exe`
con el **ffmpeg empaquetado** — el complemento le pasa el archivo tal cual lo elegiste, sin tocarlo
ni convertirlo del lado del cliente.

---

## Rendimiento real

Medido, no prometido. Con el build que trae el complemento (usa la placa de video) y el motor ya
arrancado (modelo cargado en la GPU):

| Audio | Con placa NVIDIA | Estimado en procesador |
| ----- | ----------------- | ------------------------ |
| 8 min 51 s (nota de WhatsApp real) | **~10 s** (52,7x tiempo real) | **~13 min** |

La diferencia importa apenas el audio deja de ser una nota cortita: para una hora de reunión, la
cuenta pasa de un rato razonable a bastante más que la propia reunión.

El build que se **empaqueta** con el complemento usa la placa de video (carpeta `runtime/` del
plugin, ~670 MB — `ggml-cuda.dll` sola pesa 536 MB). Si tu máquina no tiene una GPU compatible con
CUDA, o preferís no usarla, **se puede cambiar a la versión de procesador reemplazando los archivos
de `runtime/`**: son binarios de whisper.cpp descargados aparte, no hay una sola línea de código que
sepa cuál de los dos está instalado. Sin recompilar nada.

*(Nota aparte, por si la tentación aparece: pasar al modelo `large-v3` completo, en vez del
`large-v3-turbo` que trae por defecto, NO mejora la transcripción en la práctica — medido en un
audio real con dicción difícil, turbo acertó más términos técnicos y fue 3,7 veces más rápido.
«Modelo más grande = más preciso» no es una regla que valga acá.)*

---

## El glosario técnico

Antes de transcribir, el complemento le manda al motor una lista de vocabulario propio como pista
(`--prompt`). Es la diferencia entre que reconozca una sigla o un nombre propio, o que invente algo
parecido por el sonido.

Es una lista simple de palabras — a diferencia del diccionario de pronunciación del Lector, acá no
hay «de/a»: cada término se escribe **tal cual aparece en el texto**, porque lo que le interesa al
motor es reconocerlo, no pronunciarlo. Se siembra solo con unos **36 términos de arranque** (símbolos
técnicos, palabras de uso diario en documentación, y algunos nombres propios del oficio) la primera
vez que activás el complemento.

### Cómo se edita: desde la ventana de Complementos

El glosario **ya no se edita abriendo un JSON a mano**. Se edita desde **Menú → Complementos**, en el
desplegable **«Glosario técnico»** que aparece bajo la fila del plugin **Dictado**:

1. Abrí la ventana de Complementos y desplegá **«Glosario técnico»** bajo **Dictado y Transcripción
   de Voz**. El encabezado muestra cuántos términos hay (o «12 de 36» si hay un filtro puesto).
2. Arriba de la lista hay un **campo de búsqueda**: filtra a medida que escribís, sin distinguir
   mayúsculas, sobre los términos ya guardados.
3. Para agregar uno nuevo: escribilo en el campo de abajo y apretá **Enter** (o el botón
   «Agregar»). El foco se queda ahí, así que podés cargar varios términos seguidos sin soltar el
   teclado.
4. Si el término está vacío, o ya existe otro igual **sin distinguir mayúsculas** (`"Pipeline"` y
   `"pipeline"` son la misma entrada — los acentos sí distinguen: `"publico"` y `"público"` son dos
   entradas distintas), la interfaz lo avisa debajo del campo y no deja agregarlo. Cada fila tiene su
   propio botón de borrar.
5. **Nada se guarda solo.** Los cambios quedan marcados «· sin guardar» en el encabezado hasta que
   apretás **Guardar**; **Descartar** vuelve a lo que hay guardado y tira lo que estabas editando.
6. Debajo de la lista, el complemento muestra un aviso propio con el **presupuesto de tokens** del
   prompt — por ejemplo *«42 términos · 101 de 224 tokens del prompt inicial (45 %) · entran ~41
   más»* — para que sepas cuánto margen te queda antes de seguir agregando.

### El punto que más confunde: un término nuevo no entra en vigor hasta reiniciar el motor

Guardar la lista **no** reconfigura `whisper-server.exe` que ya está corriendo: el glosario se le
pasa como argumento `--prompt` al **arrancar** el proceso, así que un término recién guardado no
tiene ningún efecto sobre una transcripción o un dictado que uses a continuación. Reiniciar el motor
en el momento de guardar sería caro (mata el proceso y recarga el modelo — hasta 60 s) y, peor,
cortaría un dictado en curso sin que lo hayas pedido.

Por eso, si el motor ya está corriendo con un glosario distinto al que acabás de guardar, el mismo
aviso bajo la lista lo dice explícitamente:

> ⚠ El motor ya está corriendo con otro glosario: el prompt se le pasa al arrancar, así que estos
> términos todavía no surten efecto. Para aplicarlos, destildá y volvé a tildar «Activado» acá
> arriba — el motor se reinicia solo y tarda unos segundos en quedar listo.

En criollo: **guardar la lista no alcanza**. Para que los términos nuevos se usen, hay que
**destildar y volver a tildar «Activado»** en la fila del plugin, en esa misma ventana de
Complementos — eso apaga y reinicia el motor con el glosario que acabás de guardar.

### El archivo sigue existiendo

El JSON de respaldo sigue en:

```
%AppData%\MarkdownVault\PluginData\core.dictado-voz\glosario.json
```

```json
[
  "C#", "F#", ".NET", "README",
  "pipeline", "framework", "runtime", "endpoint",
  "Python", "JavaScript", "MarkdownVault"
]
```

Se puede seguir editando a mano con el bloc de notas — el guardado desde la ventana de Complementos
escribe exactamente este mismo formato — pero **ya no es el camino principal**: la ventana de
Complementos es la forma pensada para el uso normal, y el archivo queda como vía de respaldo o para
scripts que quieran tocarlo por fuera de la app.

### El consejo que importa: cargalo por adelantado, no reacciones a los errores

La tentación es esperar a que el motor se equivoque y recién ahí agregar el término. Es al revés:
conviene sentarse una vez y cargar los nombres de personas, sistemas internos y siglas propias de tu
proyecto **antes** de dictar, no después de corregir a mano una transcripción entera.

Ejemplo real: en la misma sesión, **`pipeline`** estaba en el glosario y salió transcrito bien.
**`Azure`** no estaba, y el motor lo escuchó como **«Shure»** (la marca de micrófonos) — un error
que ningún corrector ortográfico va a encontrar, porque «Shure» es una palabra real. Agregar
`Azure` al glosario lo arregla para siempre (una vez que reiniciás el motor); corregirlo a mano cada
vez, no.

### El techo

El `--prompt` inicial de whisper.cpp tiene un límite de aproximadamente **224 tokens** (no
caracteres — varía según el término). No es infinito. Con un glosario ya ampliado a unos
**42 términos**, el prompt ocupa cerca del **45 %** de ese techo: hay margen para seguir agregando,
pero no es ilimitado — y el indicador bajo la lista lo muestra en todo momento. Si algún día se
llena, hay que priorizar — lo que se usa seguido gana sobre lo que aparece una vez.

---

## La configuración

```
%AppData%\MarkdownVault\PluginData\core.dictado-voz\config.json
```

Se siembra sola la primera vez, con estos valores (son los que trae el código):

```json
{
  "whisperDir": null,
  "modelId": null,
  "language": "es",
  "paragraphGapSeconds": 0.6,
  "removeFillers": false,
  "dictation": {
    "enterFactor": 3.0,
    "exitFactor": 1.6,
    "absoluteEnterRms": 0.020,
    "absoluteExitRms": 0.010,
    "minSpeechSeconds": 0.30,
    "silenceSeconds": 0.70,
    "maxUtteranceSeconds": 20.0,
    "preRollSeconds": 0.25,
    "calibrationSeconds": 0.60
  }
}
```

### Transcripción de archivo

| Campo | Por defecto | Qué hace al cambiarlo |
| ----- | ----------- | ---------------------- |
| `whisperDir` | `null` (autodetecta) | Carpeta donde buscar `whisper-server.exe`, si tenés una instalación propia fuera de `runtime/`. |
| `modelId` | `null` (el modelo por defecto del catálogo) | Elegir otro modelo. Hay dos: `large-v3-turbo-q5_0` (~574 MB, el que viene) y `small-q5_1` (~181 MB). **El chico NO es "el rápido": es más lento y peor.** Ver la comparación medida más abajo. |
| `language` | `"es"` | Idioma que le pasa a whisper-server con `-l`. El default del binario es `"en"` y con audio en español produce basura fonética (verificado ejecutándolo) — por eso acá el default está fijado en español. |
| `paragraphGapSeconds` | `0,6` | Segundos de silencio entre segmentos para abrir un párrafo nuevo en el Markdown resultante. **No es un número elegido a ojo**: sale de medir un audio real de WhatsApp de 8:51. Con el 1,5 s que tenía al principio salían 6 párrafos de ~1.357 caracteres cada uno — un paredón de texto. Con 0,6 s salen 18 párrafos de ~452 caracteres, una unidad de lectura cómoda. Está calibrado sobre UN hablante: si tu ritmo al hablar es distinto, ajustalo acá. |
| `removeFillers` | `false` | Si se borran muletillas (`eh`, `este`, `o sea`, `digamos`, `viste`) del texto. Apagado a propósito: no es decisión del sistema borrar palabras que vos dijiste, sin que lo pidas explícitamente. |

### Dictado en vivo (bloque `dictation`)

Son los parámetros del detector de silencio que decide cuándo estás hablando y cuándo hiciste una
pausa. Un `config.json` editado a mano se **sanea** antes de usarse: valores fuera de rango se
recortan a algo defendible, nunca rompen el dictado.

| Campo | Por defecto | Qué hace |
| ----- | ----------- | -------- |
| `silenceSeconds` | `0,70` | Cuánto silencio hace falta para dar por terminada la frase y mandarla a transcribir. Es el número que más se siente al dictar: bajarlo corta más seguido (frases más cortas, más rápido en pantalla); subirlo espera más antes de cortar. |
| `calibrationSeconds` | `0,60` | Ventana **sorda** al arrancar el dictado, para medir el ruido de fondo de la sala antes de escuchar de verdad. Si empezás a hablar en el instante cero, perdés la primera media palabra — por eso el aviso «no hables todavía». |
| `maxUtteranceSeconds` | `20,0` | Corte forzado: ningún trozo de audio dura más que esto, hables sin pausa o no. Techo por debajo del límite del motor (la ventana de Whisper es de 30 s) para dejar margen. |
| `minSpeechSeconds` | `0,30` | Habla mínima para que algo cuente como frase y no como un ruido corto (una tos, un golpe). |
| `enterFactor` / `exitFactor` | `3,0` / `1,6` | Cuántas veces por encima del piso de ruido tiene que subir el volumen para considerar que empezaste a hablar (`enterFactor`), y cuánto tiene que bajar para considerar que terminaste (`exitFactor`). El de salida siempre queda por debajo del de entrada: si no, alguien que habla fuerte y sostenido nunca dejaría que el detector note la pausa. |
| `absoluteEnterRms` / `absoluteExitRms` | `0,020` / `0,010` | Los mismos umbrales pero en volumen absoluto (no relativo al ruido de fondo), para salas muy silenciosas donde el piso medido es casi cero. |
| `preRollSeconds` | `0,25` | Cuánto audio de ANTES de detectar que empezaste a hablar se incluye igual en el trozo — para no perderte la primera sílaba. |

---

## Dictado en vivo

**Iniciar dictado** abre el micrófono. Antes de escuchar de verdad hay una **calibración de 0,6 s**
en la que el complemento mide el ruido de fondo de la sala — no hables todavía, esa parte es sorda a
propósito y se avisa en la barra de progreso. Cuando termina, la barra de estado dice «Dictado:
escuchando. Hablá normalmente; cada pausa inserta la frase.»

De ahí en más:

- Cada vez que hacés una **pausa de ~0,7 s**, lo que dijiste se transcribe y se inserta en el
  documento, en el cursor (o reemplazando lo que tengas seleccionado — el dictado se comporta como
  escribir a máquina).
- Si seguís hablando sin pausa, hay un **corte forzado a los 20 s**: el trozo se cierra igual y se
  manda a transcribir, aunque no hayas hecho silencio.
- **Cada pausa continúa el mismo párrafo** — el dictado no abre un párrafo nuevo por cada frase
  confirmada. Recién con un silencio bastante más largo (una pausa real entre ideas) arranca un
  párrafo aparte.
- El texto se inserta en el **panel donde arrancaste el dictado**, aunque cambies de pestaña dentro
  de ese mismo panel mientras seguís hablando.

**Detener dictado** corta la sesión y libera el micrófono. Si el permiso de Windows está apagado o
no hay micrófono conectado, el complemento lo dice con un mensaje accionable (ver la sección
siguiente) y el resto del complemento sigue funcionando con normalidad.

---

### Los dos modelos, medidos

El catálogo trae dos. La intuición dice que el chico es el rápido; **medido, es al revés**. Mismo audio de 8:51, misma máquina con placa NVIDIA, mismos parámetros:

| | Tamaño | Tiempo | Confianza mediana | Peor caso | Términos técnicos |
|---|---|---|---|---|---|
| `large-v3-turbo-q5_0` *(por defecto)* | 574 MB | **8,4 s** | **-0,064** | **-0,49** | acertó los 5 |
| `small-q5_1` | 181 MB | 14,6 s | -0,214 | -1,71 | acertó 0 de 5 |

`API`, `APIs`, `pipeline`, `FPay` y `ONP`: el grande los escribió bien, el chico ninguno.

El motivo es que `large-v3-turbo` es una **destilación con el decodificador recortado**, y el decodificador es la parte cara cuando hay placa de video. Achicar el modelo no compensa arrastrar un decodificador completo: se pierde en velocidad y en precisión a la vez.

El caso legítimo del modelo chico es una **máquina sin placa de video**, donde el peso del encoder cuenta más. Eso no está medido. Si tenés placa, no lo elijas buscando velocidad.

También se probó `large-v3` completo (1 GB, sin destilar): resultó **peor que el turbo** — escribió «SAPI» por `API`, «PILAN» por `pipeline`, y se saltó una frase entera. No está en el catálogo por eso.

## Cuando algo no anda

El registro de diagnóstico vive en:

```
%AppData%\MarkdownVault\logs\plugins.log
```

Ahí queda, entre otras cosas: las transiciones del motor (arrancando → listo, con el puerto y
cuánto tardó), el progreso de la descarga del modelo, el **factor de tiempo real** de cada
transcripción (tiempo insumido ÷ duración del audio — el número que compara procesador contra
placa de video), y por qué se canceló algo si se canceló. El log filtra el ruido repetitivo del
banner de ffmpeg para que lo importante no quede enterrado entre líneas.

**Si el micrófono no arranca**, el mensaje ya viene traducido y accionable. El más común, con el
permiso de Windows apagado, dice exactamente:

> Windows tiene bloqueado el acceso al micrófono. Abrí Configuración → Privacidad y seguridad →
> Micrófono, activá «Acceso al micrófono» y, más abajo, «Permitir que las aplicaciones de
> escritorio accedan al micrófono».

**Si `whisper-server.exe` desaparece de la carpeta `runtime/`** sin que nadie lo haya tocado,
sospechá de un antivirus antes que del complemento: es un `.exe` que abre un puerto y escucha
conexiones, exactamente el patrón que un antivirus agresivo pone en cuarentena. `whisper-cli.exe`,
al lado, suele sobrevivir porque no escucha nada. Si pasa, restaurarlo de la cuarentena (o volver a
extraerlo del paquete original) lo arregla sin tocar nada más.

---

## Límites honestos

**La transcripción es reproducible**: el mismo audio, con la misma configuración, da **siempre el
mismo texto**. Esto es a propósito — el motor arranca con la opción que evita que arrastre contexto
entre ventanas, que es además lo que elimina los bucles de repetición de Whisper (sin ella, en un
audio real con dicción difícil, hasta un cuarto de las palabras terminaron siendo la misma frase
repetida una y otra vez). La consecuencia práctica es que **reintentar no sirve**: si algo salió
mal, transcribir el mismo archivo de nuevo va a dar exactamente el mismo resultado. Hay que cambiar
algo real — el audio, el idioma, el glosario, el modelo — no repetir el intento sin más.

**Con dicción muy mala hay un piso que ninguna herramienta cruza.** Si el audio original es difícil
de entender para una persona, tampoco va a ser preciso para el motor. El glosario ayuda con
vocabulario específico, no con audio de mala calidad.
