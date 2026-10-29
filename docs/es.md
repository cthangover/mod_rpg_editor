# RPG Map Editor — pinta mapas de tiles y coloca eventos

El RPG Map Editor edita los mapas de tiles del motor RPG con el juego en marcha: las tres capas de tiles, muros y pasos, el punto de inicio del jugador, los combates aleatorios y los eventos — personajes, carteles, puertas, objetos que recoger. Al guardar, el mapa se actualiza en el juego al instante, y **Jugar** te deja recorrerlo a pie.

Se abre desde la caja de herramientas (el martillo de la barra) como **Editor de mapas RPG**, sobre el mapa en el que esté el jugador.

## Una edición y su resultado en el juego

La casa del anciano de RPG Plus antes de editar:

![La casa del anciano en el juego: una habitación de madera con el anciano al fondo y un cofre a la derecha.](assets/house-before.jpg)

En el editor: la pestaña **Appearance** (Apariencias), el sprite del mercader, **Place NPC** (Colocar PNJ), un clic en una casilla libre. El evento nuevo queda seleccionado en la capa **Eventos**, con sus propiedades a la derecha:

![El Editor de mapas RPG sobre rpg_demo_game / rpg_elder_house.json: la lista Appearance con merchant · village/merchant seleccionado, el mapa con el anciano y el nuevo mercader enmarcados como eventos y marcos discontinuos alrededor de los eventos de otros archivos, y a la derecha las propiedades del mapa (tileset village, 12 × 10, punto de inicio 6, 8 mirando arriba) y el nuevo evento npc_village_merchant en 9, 3 con charset village/merchant, orientación abajo y activación por tecla de acción (E/F). La línea de estado dice que «merchant» se colocó como PNJ.](assets/editor-npc.jpg)

Tras **guardar** (Ctrl+S), el mercader está en la casa:

![La misma sala en el juego, ahora con el mercader a la derecha de la pared del fondo.](assets/house-after.jpg)

Su diálogo y comportamiento se ajustan en las propiedades del evento: un escenario, una condición, un disparador.

## Las capas y las herramientas

- **Fondo**, **Principal**, **Frente** — las capas de tiles. Fondo y Principal pueden bloquear el paso con tiles sólidos; Frente se dibuja sobre los personajes y nunca bloquea.
- **Colisiones** — se pinta sobre los tiles: *Muro* (siempre bloqueado), *Paso* (siempre transitable) o *Auto* (según los tiles).
- **Eventos** — clic para seleccionar o arrastrar, doble clic en una casilla vacía para añadir, Supr para borrar.
- **Lápiz**, **Goma**, **Relleno**; el botón derecho borra, Alt+clic toma un tile, las teclas 1–5 cambian de capa y B/E/G los pinceles.
- **Deshacer**/**Rehacer** (Ctrl+Z / Ctrl+Y), la cuadrícula, el zoom (Ctrl+rueda).

![La capa Colisiones de la casa del anciano: los muros pintados en rojo como Muro, el suelo abierto.](assets/collision.jpg)

Las pestañas de la izquierda: **Casillas** (la paleta del tileset del mapa; los objetos enteros, como una casa o un árbol, se estampan de una vez), **Personajes** (personajes con atributos de combate, colocados como enemigos), **Objetos** (colocados como objetos que recoger, con cantidad) y **Appearance** (todos los sprites de los mods cargados, colocados como PNJ).

La columna derecha reúne las propiedades del mapa: su clave de nombre, tileset, tamaño, zoom, luz fija y atmósfera, el punto de inicio con su orientación, y los combates aleatorios (cada ~N pasos, los grupos de enemigos tipo `slime; wolf,slime`).

## Vista superior e isométrica

El editor dibuja el mapa igual que lo dibuja el juego. El selector de vista de la barra cambia solo la vista previa; los datos del mapa quedan igual. La casa en la vista previa isométrica:

![La casa del anciano en la vista previa isométrica del editor: la misma sala dibujada en isométrico, la paleta del tileset a la izquierda.](assets/editor-isometric.jpg)

La vista del juego la fija el ajuste de la colección `tile.renderer` (`topdown` o `isometric`); `--editor-renderer=isometric` abre el editor directamente con la vista previa isométrica.

## Qué archivos se escriben

- El mapa en sí se guarda en su `maps/<id>.json`.
- Los eventos van al archivo de eventos del propio mapa, en el mismo mod (`map_events/<mapa>/…`). Los eventos que otros archivos u otros mods añaden al mapa se muestran atenuados con marcos discontinuos y nunca se reescriben; sus ids no se pueden reutilizar.
- **+** junto a la lista de archivos crea un mapa nuevo en el mod elegido; tras guardar, el juego lo conoce como escena.
- Solo se pueden escribir los mods instalados como carpetas.

## Si algo falla

1. **Jugar dice que el juego no conoce la escena.** El mapa necesita un `scenes/<id>.json` en su mod.
2. **Un evento no se deja seleccionar.** Pertenece a otro archivo o mod (marco discontinuo).
3. **Un PNJ no habla.** Su evento no tiene escenario, o su activación no es la tecla de acción.
4. **Al guardar cambian líneas que no tocaste.** El editor escribe los archivos en su propio orden y añade valores por defecto de los eventos, así que un diff puede mostrar más que la edición.
