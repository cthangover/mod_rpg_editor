# Editor de mapas RPG — pintar mapas y colocar objetos

El Editor de mapas RPG cambia los mapas de losetas del mundo RPG mientras el juego está en marcha. Se pintan el suelo y los muros, se colocan sprites y personajes en casillas, se pinta la colisión, se mueve el punto de aparición y se configuran los encuentros aleatorios. **Guardar** escribe los archivos del mapa, y el juego recorre el mapa nuevo al momento, sin reiniciar.

Se abre desde la caja de herramientas (el martillo de la barra) como **RPG Map Editor**. Se abre en el mapa que muestra el juego; para abrir otro, use el selector de mapas.

## Crear eventos y objetos desde el mapa

Pulsa **Evento** y arrastra un rectángulo sobre las casillas. Elige **Al pisar** o **Al usar (E / F)**, escribe comandos, pulsa **Aplicar evento** y **Guardar**. No necesitas crear un objeto ni un archivo de guion aparte.

Mueve o redimensiona la región con Seleccionar, en ambas proyecciones. Las opciones permiten una condición, un guion compartido o un teletransporte. Para usarlo una vez, escribe `self.done != 1` como condición y termina el guion con `flag self.done 1`. Los eventos no tienen imagen y no bloquean el paso por defecto.

Pulsa **Nuevo objeto…** bajo la paleta para abrir su formulario en el mod del mapa. Configura la imagen y la interacción, guarda y elige el objeto en la paleta actualizada para colocarlo.

![Evento del mapa: región, activador y guion en un editor.](assets/map-event.png)

## Qué se ve en la ventana

![El Editor de mapas RPG sobre la casa del anciano de RPG Plus: la barra con el selector de mapa, Save, Undo, Redo y las herramientas; la paleta de objetos a la izquierda con la pestaña Surfaces; el mapa en el centro; el inspector a la derecha con el tamaño, el punto de aparición, los enlaces de teletransporte y los encuentros.](assets/rpg-editor-open.jpg)

- **Barra de herramientas** — el mod y el archivo del mapa (**+** crea un mapa nuevo), **Save**, **Undo**, **Redo**, las siete herramientas, el selector de código de colisión, el selector de proyección (**Top-down** o **Isometric**) y **Fit**, que muestra el mapa entero.
- **Paleta** (izquierda) — cuatro pestañas por forma: **Surfaces**, **Segments**, **Sprites**, **Actors**, y **Characters** para personajes con `Charset`. Cada una tiene búsqueda y miniaturas.
- **Mapa** (centro) — el mapa dibujado por el propio renderizador del juego. Los marcos finos muestran las casillas que ocupa cada colocación; la colocación seleccionada aparece rellena.
- **Inspector** (derecha) — la colocación seleccionada, las propiedades del mapa, sus enlaces de teletransporte y los encuentros.

El selector de proyección solo cambia el dibujo. El mapa es el mismo en ambas proyecciones, y se puede cambiar mientras se edita:

![La misma casa del anciano en la proyección isométrica.](assets/rpg-editor-isometric.jpg)

## Las herramientas

- **Select** — un clic sobre una colocación la selecciona; arrastrar la mueve a otra casilla; **Delete** la quita; **Esc** quita la selección.
- **Paint** — elija una superficie o un segmento en la paleta y haga clic o arrastre sobre las casillas. Las superficies pintan el suelo y los segmentos la capa de estructura. El botón derecho borra.
- **Place** — elija un sprite o un actor y haga clic en una casilla. Un fantasma del objeto sigue al puntero; se vuelve rojo donde el objeto bloquearía casillas. La pestaña **Personajes** (la última) coloca un personaje como objeto actor: el objeto se escribe en el mod del mapa (`objects/characters.json`) al elegirlo, y un personaje con escenario de unión (véase el editor de personajes) se une al grupo al hablar con él.
- **Collision** — pinta la transitabilidad de las casillas: **As objects (.)** lo deciden los objetos, **Blocked (#)** bloquea la casilla, **Forced open (o)** la deja libre a la fuerza.
- **Spawn** — un clic en una casilla pone allí el punto de aparición del jugador. El botón **Pick a cell** del inspector cambia a esta herramienta.

Un trazo de superficie muestra sus casillas mientras arrastra. Cada trazo, colocación, movimiento, cambio de colisión y cambio del punto de aparición es un paso de deshacer.

![Pintando una superficie: en la pestaña Surfaces está elegida la tierra, la herramienta Paint está activa y la casilla bajo el puntero aparece marcada.](assets/rpg-editor-paint.jpg)

Para colocar un sprite, elíjalo en la pestaña **Sprites**, active **Place** y haga clic en la casilla. La colocación nueva queda seleccionada y el inspector la muestra al momento. Un clic sobre una colocación existente con **Select** muestra sus propiedades en el inspector:

![Colocación en la casa del anciano seleccionada con Select: el barril aparece relleno de amarillo y el inspector muestra el objeto, el id, que es colocación propia del mapa, la casilla y el botón Remove.](assets/rpg-editor-placement.jpg)

![Colocando un sprite: en la pestaña Sprites está elegido un barril y la herramienta Place activa; el fantasma del barril está sobre la casilla bajo el puntero.](assets/rpg-editor-place.jpg)

## El inspector

- **Colocación** — el nombre y el id del objeto, y de dónde viene la colocación. Las colocaciones propias del mapa dicen *This map's own placement*. Una colocación de otro mod muestra el nombre de ese mod: sus cambios se escriben como parche en su mod, y el archivo del otro mod no cambia. Puede cambiar **Cell X** y **Cell Y**, **Remove** la quita, o **Reset** quita el parche de su mod para que el juego vuelva a ver la colocación del dueño.
- **Interacción** — qué ocurre cuando el jugador la usa o la pisa. **Hace** elige **Entrega un objeto** (el selector de objeto y **Cantidad**), **Lleva a otra casilla o mapa** (el selector de mapa, el destino X e Y, la mirada y **Elegir la casilla en el mapa**), **Abre un diálogo** (el selector de escena), **Abre una tienda** (el selector de tienda) o **Nada**. **Cuándo** elige usarla (E o F mirando al objeto) o pisarla. La elección sustituye la interacción propia del objeto solo en esta colocación, así que también se sustituyen los comandos propios del cofre. Para elegir la casilla de otro mapa, guarde antes: el editor abre ese mapa, usted hace clic en la casilla de llegada y vuelve a abrirse el mapa de origen con el destino escrito (Ctrl+S guarda; Esc cancela). En una colocación de otro mod que ya tiene su propia interacción, una elección que descarta alguno de sus campos se rechaza, porque un parche no puede descartarlo: cambie el archivo del propietario.
- **Mapa** — el tamaño en casillas, el punto de aparición (**Elegir casilla**), la **Variante** (el aspecto de los objetos en esta variante del mapa) y la **Atmósfera** (por ejemplo `woodland`; ninguna para ninguna). Ambas se eligen de listas.
- **Enlaces de teletransporte** — adónde llevan los teletransportes de este mapa y cuáles llevan aquí. En el mapa, una flecha azul va de una puerta a su casilla de destino en este mapa, o sale hacia el nombre del otro mapa; una verde llega desde otro mapa; una roja indica un destino roto. La lista muestra los mismos enlaces (→ sale, ← llega). Haga clic en un enlace para abrir el mapa de destino. Una marca roja significa destino roto: no existe el mapa, la casilla está fuera del mapa o está bloqueada.
- **Encuentros** — solo aparecen si el juego tiene la integración de combates aleatorios (`rpg__battles`). **Steps per battle** es la media de pasos entre combates (0 los desactiva). **Enemy groups** admite un grupo por línea, con los enemigos separados por comas; **Apply encounters** los escribe.
- **Añadir grupo de enemigos...** (en la misma sección) añade una línea con ese enemigo como grupo propio; edite la línea para poner varios enemigos en un grupo.
- **Vista previa del estado** — para una colocación cuyo objeto tiene estados o una interacción con flags `self.`, el inspector muestra esos flags como casillas. Marcar un flag muestra el estado que se le aplica, según la regla del propio juego. Es solo una vista previa: los valores se guardan mientras la ventana está abierta, no se guarda nada y los flags del juego no cambian. El lienzo sigue dibujando el aspecto propio del objeto: el sprite no cambia con el estado.

![El mapa de la aldea de rpg_plus_game: el inspector enumera sus enlaces de teletransporte, salientes (→, con el mapa y la casilla de destino) y entrantes (←).](assets/rpg-editor-village.jpg)

## Guardar y qué archivos cambian

- **Save** (Ctrl+S) valida primero el mapa y sus colocaciones. Un problema detiene el guardado, y la línea de estado indica cuál es. Si no, el editor escribe solo los archivos que han cambiado, en el mod elegido.
- Un mapa nuevo, creado con **+** (16 × 12 casillas, punto de aparición en 1,1), se escribe al primer guardado junto con su escena. Desde entonces el juego conoce el mapa al momento.
- Puede abrir el mapa de un mod del que depende. Pintar ese mapa se escribe como trazos en un parche de su mod; el archivo del mapa del dueño queda idéntico byte a byte.
- No se añaden colocaciones a un mapa de otro mod. Solo se puede guardar en mods instalados como carpeta.

## Pendiente

Quedan pendientes renombrar mapas/colocaciones actualizando referencias, abrir el objeto colocado seleccionado en su editor y formularios de estados/variantes de una colocación. La selección por rectángulo, copiar/pegar, máscaras de efectos y selectores de interacción ya funcionan.

## Si algo sale mal

1. **El mapa no está en la lista de archivos.** No está en el mod elegido ni en sus dependencias.
2. **Save no hace nada y la línea de estado muestra un problema.** Un mapa o una colocación no son válidos; corrija la casilla o el valor que indica la línea.
3. **El juego no conoce un mapa nuevo.** Guarde primero: el mapa y su escena se escriben solo en el primer guardado.
4. **Una colocación se rechaza.** El objeto no es un sprite ni un actor, la casilla está fuera del mapa o el mapa pertenece a otro mod.
5. **No hay panel de encuentros.** El juego no tiene combates aleatorios (`rpg__battles`).

## Eventos del mapa y combates opcionales

Las zonas invisibles se guardan en `Events` del mapa: `Id` local, `X`, `Y`, `Width`, `Height`, `Trigger`, `When`, `Solid` y `Scenario` con `Commands` o un `Teleport` independiente. No necesitan objeto ni colocación. `self.done` pertenece a `<map>/<event>:done`; conserve el ID. En el editor elija Event, dibuje el rectángulo, escriba las órdenes, Apply event y Save.

## Selección, efectos y prueba

En Select, arrastra un rectángulo para seleccionar varias colocaciones; Ctrl+A selecciona todas. Ctrl+C copia y Ctrl+V pega en la casilla bajo el puntero, conservando los desplazamientos. Cada operación se deshace en un paso. Las flechas de teletransporte permiten mover la salida y el destino; una llegada desde otro mapa se guarda en su archivo o en un parche del mod elegido.

En **Effects**, elige un perfil y pinta su máscara con LMB; RMB borra. **Open map editor** de Effects Editor abre esta herramienta con el perfil elegido. Una máscara vacía se elimina.

En **Collision** las casillas bloqueadas se ven rojas en ambas proyecciones. LMB pinta el código elegido; RMB escribe `o` y abre paso incluso a través de objetos. `#` bloquea y `.` vuelve a delegar en `Solid` y `Footprint.Solid`. Un trazo es un paso de Ctrl+Z/Y.

Los eventos sin imagen tienen cuatro tiradores de esquina en Select. Arrastrar cambia `X`, `Y`, `Width`, `Height` como un paso de deshacer; las colocaciones antiguas cambian `Footprint.Size`. El rectángulo no sale del mapa y mide al menos una casilla.

Save añade a `depends` solo las dependencias que faltan para los objetos y recursos usados; los archivos de los propietarios permanecen intactos. **Play from here** guarda y entra en el mapa en la última casilla pulsada (o el punto inicial); necesita una partida RPG en marcha. La pestaña **Characters** se suma a Surfaces, Segments, Sprites y Actors y coloca personajes que tengan `Charset`.

**Gives an item** repite la entrega mientras no se añada un flag y un estado o condición de uso único. El editor de eventos permite escribir las órdenes y terminar con `flag self.done 1`, usando `self.done != 1` como condición.

Para un primer juego: crea un mod desde el esquema de RPG Plus, inicia el juego, crea y guarda un mapa, pinta suelo y coloca un cofre. Configura su interacción en el inspector; añade un evento puerta y un NPC con escenario. Un personaje con `Charset` y **Joins the party when talked to** se coloca desde Characters: su diálogo se reproduce antes de unirse. Usa Play from here y prueba los objetos con E/F.
