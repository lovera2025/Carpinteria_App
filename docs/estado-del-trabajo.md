# Estado del trabajo — la caja del taller

Última actualización: **2026-09-06**.

**Las cuatro tandas están hechas y mergeadas en `master`** —A2, A, B y C—, con la suite en
verde. **Nada está publicado**: `master` está adelante del remoto y no se empujó ningún tag.

**La publicación está en pausa a propósito.** Antes de sacar la versión falta **revisar
Inventario entero** (ver «Lo que falta»). La idea es no ir tirando actualizaciones cada rato:
se junta todo lo que haya que cambiar y sale **una sola versión** con todo adentro.

Ojo al cambiar a una rama vieja: la base local de prueba ya está en **esquema v14**, así que
una rama que maneje hasta v13 no la abre. No es una falla —el guardián avisa y no toca los
datos— y al carpintero no le puede pasar actualizando: su base va siempre hacia adelante.

Este documento existe para poder retomar desde cero. Si arrancás una conversación nueva, leé esto primero.

---

## De dónde salió todo esto

Una grabación del carpintero que usa la app (Metro Carpintería, en producción, una sola notebook). Reportó tres cosas que resultaron ser cinco problemas distintos:

1. **Las señas y pagos a cuenta no aparecían en su Caja.** Un cobro solo entraba si el medio era Efectivo; una transferencia bajaba el saldo del cliente y no dejaba rastro en ningún lado, sin aviso.
2. **Los movimientos no decían de dónde venía la plata.** *"Yo sé que me pagó María"*, pero el renglón decía solo `Seña: Mostrador`.
3. **No podía aprobar un presupuesto sin materiales cargados.**
4. **«Al darle recalcular se descajetaba todo»** — el precio que pactaba con el cliente se borraba solo.
5. Los **16.000 sin explicar** que había reportado en agosto.

Y debajo de todo eso, la causa de fondo: **la app modelaba una caja registradora y él tiene una caja fuerte.**

---

## Decisiones de producto (no se deducen del código)

Están en la memoria del proyecto, en `caja-es-caja-fuerte-no-registradora`. Resumen:

- **La caja no tiene sesiones.** No se abre ni se cierra, no hay arqueo. Es un saldo que corre y nunca se reinicia.
- **Entra toda la plata cobrada**, sea cual sea el medio.
- **La plata es una sola.** No se parte el total en «en el banco» / «en el cajón» — el taller no hace esa división. El medio se muestra **en cada movimiento**, no en el total. *(Esto se probó y se revirtió: ver «Lo que se descartó».)*
- **Aprobar un presupuesto no mueve plata.** Lo corrigió él expresamente: aprobar es que el cliente dijo que sí, no que pagó. Solo «Registrar cobro» mueve la caja.
- **Nada se descuenta por estar «marcado».** La caja se mueve cuando él registra que cobró o pagó.
- **Se corrige, no se borra.** Ningún borrado duro: las correcciones compensan y los dos renglones quedan visibles.
- **Lo ya cargado no se toca.** Los pagos que marcó a mano antes quedan como están: sin etiqueta nueva, sin cartel, sin registro retroactivo.
- **Sin ceremonia.** Pedirle que lea un desglose y apriete un botón para seguir usando la app es ceremonia. Los avisos aparecen solo cuando hay algo real que decir.

Reglas de trabajo que puso él:

- **Los números primero.** Un bug de pantalla molesta; uno de números le hace perder confianza en todo lo demás.
- **Nada de errores en silencio.** Ver «Barandas» abajo.
- **No se borra ningún test.** Las aserciones se pliegan en los archivos que ya existen.

---

## Lo que está hecho

Todo commiteado y mergeado en `master`, **300/300 tests en verde**, y probado abriendo la app
contra la base local real. Las cuatro tandas van en orden: A2, B, A y C.

### Tanda A2 — el precio pactado (rama `precio-pactado-no-se-pisa`)

`feacfe2` · **Migración v13**

`Project.Budget` guardaba dos cosas que no se distinguían: la salida de la fórmula y el precio acordado con el cliente. Como `SaveCalculation` corre **en cada salida de campo de la calculadora**, alcanzaba con tocar cualquier dato para perder el precio negociado.

- `Project.IsPriceManual` separa las dos cosas.
- `SaveCalculation` y `SaveCommercialTerms` ya no pisan el total pactado.
- Con IVA, el bloque comercial se arma **al revés desde el total pactado**.
- `QuoteService.RestoreCalculatedPrice(projectId)` es su propio método: `SetFinalPrice` con el número calculado seguía marcándolo como manual.
- El relleno solo marca los presupuestos que repartieron el recorte (los únicos que dejaron rastro). Los que solo redondearon no se pueden reconocer sin recalcularlos, y marcarlos por las dudas congelaría precios que deben seguir a la fórmula.

### Tanda B — la caja fuerte (rama `caja-fuerte`)

`7db60ac` · **Migración v14** — el núcleo

- Todo cobro asienta en Caja, con su medio. Se fue el `if (method == Cash)`.
- `CashMovement` gana `Method`, `Origin`, `ProjectId`, `ProjectPaymentId`, `EmployeeId`, `ProjectLaborLineId`. `CashSessionId` pasa a nullable.
- La migración **no borra nada**: rellena el origen de lo ya cargado, convierte aperturas y diferencias de arqueo en movimientos, y asienta los cobros que nunca entraron **con su fecha original**. Excluye los que ya tenían movimiento (si no, cada seña en efectivo se duplicaría).
- Anular un cobro pasa a **baja lógica** (`CancelledAtUtc`, `CancelReason`). Hubo que revisar **ocho lugares** donde se suman cobros.
- `CashRegisterService` reescrito: se van las sesiones, entran `GetBalance`, `GetMovements`, `CorrectAmount`, `UpdateReason`, `GetPaidByLaborLine`.
- Blast radius arreglado: `ReportService` y `HomeViewModel` informaban estado de sesiones que ya no existen.

`7b36771` · el aviso de aperturas dudosas
`290accd` · **la pantalla salía en blanco** — faltaba el trigger de opacidad
`639f016` · **bindings rotos** dejan de pasar en silencio
`a0c7ec3` · un cobro anulado se lee como anulado y no puede emitir recibo
`ba0631c`, `f09e61e`, `e4fd3b7` · la tarjeta de la caja, simplificada en tres pasadas

`1c1c7a5` · **el saldo deja de partirse por medio, esta vez en serio**

Se había sacado de la tarjeta de Caja, pero sobrevivía en las dos pantallas que se
reescribieron para quitarles las sesiones. Inicio decía «En efectivo: …» y Reportes mostraba
«En efectivo — lo que tendría que haber en billetes». Con la base de prueba daba **−$ 3.500**:
un imposible en billetes, porque los gastos quedan en efectivo y los cobros entran con su
medio real. `CashOnHand` sigue existiendo porque dos tests la usan para verificar que una seña
por transferencia no suma al efectivo.

### Tanda A — aprobar sin materiales (en `master`)

`47b815b` · Se fue la validación. Sin precio se sigue bloqueando; sin materiales ya no.

- Los tres textos que prometían descontar dejaron de prometerlo cuando no hay nada que
  descontar: el diálogo, el botón (vuelve a ser «Aprobar» a secas) y el aviso posterior.
- Probado en la app: presupuesto de $ 45.000 con cero materiales, aprobado, y después
  devuelto a presupuesto con «Cancelar trabajo» — que es la prueba de que aprobar **no** es
  irreversible, el argumento con el que se justificaba la validación.

---

### Tanda C — la liquidación de los terminados

`bf72917` · Lo último que pidió en la grabación: *"una vez terminado tendría que ir a
Proyectos terminados… ahí tiene que estar el desglose… cuánto es del desperdicio, cuánto de
las herramientas, cuánto es lo mío, cuánto lo de Alejandro y cuánto lo de Javi. Entonces yo a
Alejandro le pongo Pagar."*

- **Vive adentro de Caja, no en el menú.** Lo pidió Maximiliano: la barra lateral ya tenía
  diez entradas. La tarjeta de Caja dice cuánto falta pagar y abre la liquidación; se vuelve
  con un botón. Además es donde corresponde: pagarle a un operario es plata que sale de ahí.
- **El pago ES el movimiento de caja**, con proyecto, empleado y línea de mano de obra
  anotados. Nada de booleanos que puedan discrepar, y pagar en varias veces sale solo.
- **Saldado es `pagado >= le toca`**, no `==`.
- El importe viene precargado con lo que falta y es editable. Pagar de más no se bloquea:
  puede ser un adelanto, y la fila lo dice.
- **El jefe no aparece**: no es línea de mano de obra, lo suyo es un egreso normal.
- El tilde de «pagado» se fue de Proyectos y de Personal. El dato viejo **no se borra**, pero
  deja de mostrarse donde ahora podría contradecir a la liquidación. Personal pasa a mostrar
  **«Le debés»** con plata, sacada de la misma cuenta que la liquidación; antes decía
  «A cobrar: 1», un conteo de tildes en una columna que se lee como pesos.
- Archivar un trabajo con jornales sin pagar lo avisa en el diálogo.
- Siete tests nuevos, en `CommercialTests`, que es donde vive la plata.

`c504399` · **El historial de Inventario dejaba de mostrarse entero** — ver la baranda 3.

---

## Barandas nuevas (por qué ya no falla en silencio)

WPF falla callado de tres formas, y las tres ahora tienen test:

1. **Pantalla invisible.** Cada vista arranca en `Opacity 0` y un trigger la muestra. Si se pierde, la pantalla se dibuja entera y no se ve nada — no falla al compilar, no tira excepción, no sale en el log. Un test recorre las diez pantallas y exige el trigger. *Verificado sacándolo a propósito.*
2. **Binding roto.** Un binding a una propiedad que no existe deja el valor por omisión y sigue. Un test escucha las trazas de WPF mientras dibuja las diez pantallas, cada una con su ViewModel. **Encontró uno al primer intento.**
3. **Lista vacía.** El estilo global de `ListViewItem` dibujaba las filas con un `GridViewRowPresenter`, que solo entiende columnas de un `GridView` e **ignora el `ItemTemplate`**: la lista quedaba con sus renglones y ni una letra. Acá no hay binding roto que rastrear —el binding está bien—, la pantalla no está invisible y el log no dice nada. Un test dibuja una lista con `ItemTemplate` y exige ver su contenido. *Verificado sacando el arreglo a propósito: dice «se dibujó: NADA».*

Además: **el saldo se calcula en un solo lugar** (`CashRegisterService.Signed`) y **se suma en memoria, nunca con `SUM()` de SQL** — `Amount` es `TEXT` y SQLite lo pasaría por punto flotante, devolviendo un número parecido y mal.

---

## Lo que falta

### Revisar Inventario, sección por sección (lo próximo)

Lo pidió Maximiliano y **es lo que bloquea la publicación**. El plan concreto se arma con él
antes de tocar nada; esto es lo que se sabe hasta acá:

- **El historial ya se arregló** (`c504399`), pero ese arreglo salió de mirar la pantalla de
  costado mientras se revisaba otra cosa. La sección nunca se revisó entera.
- Falta pasar por todo: stock y mínimos, alta y edición de productos, entradas y salidas a
  mano, archivar y restaurar, y el cruce con Presupuestos y Proyectos —que es donde el stock
  se mueve solo al aprobar y al cancelar un trabajo.
- El criterio de siempre: **primero los números**. Un stock que miente vale más que un
  cartel mal escrito.

### Pendientes de publicación

**El código está listo; la publicación espera a la revisión de Inventario.** La decisión es
juntar todo lo que haya que cambiar y sacar **una sola versión** —A2, A, B, C y lo que salga
de Inventario— en vez de ir tirando actualizaciones cada rato.

- Falta acordar el número (el último tag es `v1.9.1`; se propuso `v1.10.0`) y empujarlo.
- **Cada tag se autoinstala solo en la notebook del taller.** Confirmar con Maximiliano antes de empujarlo, siempre.
- ~~Recorrer las pantallas que se tocaron.~~ **Hecho el 2026-09-06**, las siete, y otra vuelta
  con lo nuevo. Salieron dos problemas: el saldo por medio (`1c1c7a5`) y el historial de
  Inventario (`c504399`), los dos arreglados. Lo demás cierra: los cobros de Clientes suman
  exacto contra lo que Caja dice que entró, el precio pactado aguanta que le toquen la
  calculadora, y pagarle a un operario baja el saldo de la caja por el importe justo.
- **El susto de la base «más nueva que el código» es solo de escritorio.** Pasa al pararse en
  una rama vieja teniendo la base local ya migrada. Al carpintero no le puede pasar por una
  actualización: su base va de v12 para arriba, y el guardián solo salta al revés.
- **Opcional: ensayar la migración con la base del carpintero.** La app ya hace respaldo al cerrar (hasta 30 copias). Cierra la app antes de copiar (la base corre en modo WAL). Guardarla en `.local/` — **agregar `.local/` al `.gitignore` antes**, tiene nombres y teléfonos de sus clientes.

---

## Lo que se descartó, y por qué

Para no volver a proponerlo:

- **Partir el saldo en «en el banco» / «en el cajón».** Se implementó y se sacó: el taller no hace esa división, la plata es toda de uno. **Volvió dos veces** — quedó vivo en Inicio y en Reportes hasta `1c1c7a5`. Si aparece de nuevo, mirar el comentario de `CashOnHand`, que es de donde rebrotaba.
- **Una tarjeta de «revisá el saldo y confirmalo»** después de migrar. Se implementó y se sacó: era ceremonia, y repetía lo que el historial ya dice renglón por renglón.
- **Mostrar «Entró / Salió» al lado del saldo por medio.** Cuatro números para decir dos cosas, y dos de ellos dando igual sin explicación.
- **Registrar retroactivamente en Caja los pagos a operarios que él ya marcó.** Serían movimientos con fecha de hoy por plata que salió hace semanas.
- **Borrar los 16.000.** Si son un monto de apertura, borrarlos le descuadra el saldo real.

---

## Datos útiles

- **Base de pruebas local**: `Documentos\MetroCarpinteria\data\carpinteria.db`. Tiene datos de tecleo (`Casdasdasdasdasdasd`, `blabla`, `wwwwwmax`) — **no sirve para validar la migración de la caja**, no tiene historia real que contrastar.
- Respaldo previo a la v14: `Documentos\MetroCarpinteria\PRE_v14_*.db`.
- La base quedó con datos de prueba metidos el 2026-09-05: un egreso de $3.500 y una seña de $100.000.
- El 2026-09-06, probando la Parte A, el presupuesto `wwwww` (de `max`) quedó con precio
  **$ 45.000** donde antes decía «—». Se le borró el jornal para dejarlo como estaba, pero el
  total guardado no se limpia solo: el paso 2 avisa «falta el valor del jornal» y la barra de
  abajo sigue mostrando el importe. Es un rincón chico y viejo, nadie lo pidió.
- También del 2026-09-06, probando la liquidación: se le pagaron **$ 5.000 a Javier** por el
  trabajo `asdasdasd`. Es un egreso de caja de verdad, así que no se borra —se corrige, no se
  borra—; queda como el primer pago de mano de obra registrado.
- **Correr todo**: `dotnet build -warnaserror` y después `dotnet run --no-build` en `tests\MetroCarpinteria.SmokeTest`.
- **Abrir la app**: `src\MetroCarpinteria.App\bin\Debug\net8.0-windows\MetroCarpinteria.exe`. Cerrarla antes de recompilar o el build falla por archivo bloqueado.
- El plan completo original está en `~/.claude/plans/fijate-esto-me-dijo-jaunty-penguin.md`.
