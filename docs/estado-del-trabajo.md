# Estado del trabajo — la caja del taller

Última actualización: **2026-09-06**.

**Las cinco tandas están hechas** —A2, A, B, C y la revisión de Inventario—, con la suite en
verde. **Nada está publicado**: no se empujó ningún tag, y `master` está adelante del remoto.

Las cuatro primeras están en `master`; la revisión de Inventario está en
`inventario-revision`, lista para mergear. La idea sigue siendo la misma: **una sola versión
con todo adentro**, en vez de ir tirando actualizaciones cada rato.

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

Todo commiteado, **307/307 tests en verde**, y probado abriendo la app contra la base local
real. Las tandas van en orden: A2, B, A, C y la revisión de Inventario.

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

### Tanda D — la revisión de Inventario (rama `inventario-revision`)

Se hizo porque el bug del historial apareció **de costado**, mirando la pantalla mientras se
revisaba otra cosa. Si uno así sobrevivió sin que nadie lo notara, la sección merecía una
pasada entera antes de publicar.

`cc370d2` · **Migración v15** — dos datos que se leían del producto vivo quedan congelados

- La **unidad de cada movimiento**. Antes salía de `Products.Unit`, así que corregir la unidad
  de un producto —lo que uno hace al notar que la cargó mal— reescribía todo el pasado: un
  movimiento de 1500 u. pasaba a leerse como 1500 m².
- El **costo de cada material asignado** a un trabajo. Salía de `Products.CostPrice`, así que
  lo gastado en un mueble de agosto cambiaba solo en octubre al subir la melamina.
- El relleno usa lo que el producto dice hoy: es la mejor verdad disponible, y lo que ya se
  haya cambiado alguna vez no se puede recuperar ni se inventa.

`af11ef2` · **El material cargado después de aprobar lo decide él**

Aprobar sin materiales (tanda A) volvió normal un camino que antes casi no pasaba: cargar la
madera después, desde Proyectos. Ese camino descontaba stock, pero la plata la resolvía la app
sola y siempre para el mismo lado —salía del bolsillo del taller, sin preguntar—.

- Al asignar material a un trabajo con precio acordado, **pregunta**. Si lo pone él, el precio
  no se mueve. Si se lo suma al cliente, la app propone material + desperdicio + desgaste con
  los porcentajes de ese trabajo, y él puede cambiar el número.
- Cobrarlo marca el precio como **pactado a mano**: lo decidió él y ningún recálculo lo pisa.
- Quitar el material devuelve las dos cosas, stock y recargo. Cancelar el trabajo también.
- En Terminados, cuando gastó más de lo cotizado, una línea lo explica: cuánto cotizó, cuánto
  gastó, y si esa diferencia se la sumó al cliente o sale de su ganancia.

`49a1367` · **Lo que salió de la recorrida, pantalla por pantalla**

- Con «Solo alertas» puesto y nada bajo el mínimo, Inventario decía «Todavía no hay productos.
  Cargá el primero»: le avisaba que se le borró el inventario. **Él trabaja con los filtros
  puestos**, así que ahora el vacío nombra el filtro y dice cuál destildar.
- Cambiar la unidad de un producto con stock avisa: el historial está a salvo, pero el número
  del stock no se convierte.
- Archivar un producto con stock dice cuánto se va a dejar de contar.
- El historial sin producto elegido trae los de todos, y ahora lo dice.
- Se avisa cuando la lista está cortada en los últimos 30.

**Lo que la recorrida NO encontró**, y conviene que quede escrito: ninguna cantidad se compara
del lado de SQL —todas pasan por `AsEnumerable`, que es la trampa de las columnas `TEXT`—, las
validaciones de stock insuficiente y producto archivado se respetan y se explican bien en
pantalla, el botón gris de Eliminar dice por qué está gris, y `ApplyPendingStock` **sí** tenía
test, al revés de lo que suponía el plan.

---

## Barandas nuevas (por qué ya no falla en silencio)

WPF falla callado de tres formas, y las tres ahora tienen test:

1. **Pantalla invisible.** Cada vista arranca en `Opacity 0` y un trigger la muestra. Si se pierde, la pantalla se dibuja entera y no se ve nada — no falla al compilar, no tira excepción, no sale en el log. Un test recorre las diez pantallas y exige el trigger. *Verificado sacándolo a propósito.*
2. **Binding roto.** Un binding a una propiedad que no existe deja el valor por omisión y sigue. Un test escucha las trazas de WPF mientras dibuja las diez pantallas, cada una con su ViewModel. **Encontró uno al primer intento.**
3. **Lista vacía.** El estilo global de `ListViewItem` dibujaba las filas con un `GridViewRowPresenter`, que solo entiende columnas de un `GridView` e **ignora el `ItemTemplate`**: la lista quedaba con sus renglones y ni una letra. Acá no hay binding roto que rastrear —el binding está bien—, la pantalla no está invisible y el log no dice nada. Un test dibuja una lista con `ItemTemplate` y exige ver su contenido. *Verificado sacando el arreglo a propósito: dice «se dibujó: NADA».*

Además: **el saldo se calcula en un solo lugar** (`CashRegisterService.Signed`) y **se suma en memoria, nunca con `SUM()` de SQL** — `Amount` es `TEXT` y SQLite lo pasaría por punto flotante, devolviendo un número parecido y mal.

---

## Lo que falta

### El tilde viejo de «pagado» y el riesgo de pagar dos veces (sin resolver)

**Es lo único de plata que queda abierto, y conviene cerrarlo antes de publicar.**

Antes de la tanda C, marcar un jornal como pagado prendía un booleano
(`ProjectAssignment.IsPaid`) sin mover un peso. Ese dato **no se borró** —es su registro— pero
la liquidación **no lo mira**: para ella lo único que cuenta son los movimientos de caja.

Entonces, si él ya marcó a mano el jornal de alguien en un trabajo que además tiene operarios
cotizados, Terminados se lo va a mostrar como pendiente. Si lo paga desde ahí, **lo paga dos
veces**.

No es teórico: en la base de prueba pasa. La asignación de Javier en `asdasdasd` figuraba como
«Pagado», y Terminados lo mostró debiéndole $ 20.000. La ventana es chica —las líneas de mano
de obra son recientes y los trabajos viejos no tienen— pero es plata, y el riesgo aparece la
primera vez que él abre Terminados.

Tres salidas posibles, y la decisión es de él:

- Que la liquidación tome el tilde viejo como saldado.
- Que lo muestre como aviso en la fila, sin tocar los números.
- Dejarlo así, si él sabe cuáles ya pagó.

### Pendientes de publicación

**El código está listo.** Falta mergear `inventario-revision` a `master`, acordar el número
(el último tag es `v1.9.1`; se propuso `v1.10.0`) y empujarlo. Sale **una sola versión** con
las cinco tandas adentro.
- **Cada tag se autoinstala solo en la notebook del taller.** Confirmar con Maximiliano antes de empujarlo, siempre.
- ~~Recorrer las pantallas que se tocaron.~~ **Hecho el 2026-09-06**, las siete, más la
  recorrida completa de Inventario. Lo que cierra: los cobros de Clientes suman exacto contra
  lo que Caja dice que entró, el precio pactado aguanta que le toquen la calculadora, pagarle
  a un operario baja el saldo de la caja por el importe justo, y cargar material después de
  aprobar mueve el stock y el precio como corresponde según lo que él elija.
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
- Y probando el material extra en ese mismo trabajo quedaron dos movimientos de stock del
  Tornillo, una salida de 2 u. y su devolución: se asignaron cobrándoselos al cliente y después
  se quitaron, para ver que el precio subía y volvía. El stock y el precio quedaron como
  estaban; los dos renglones del historial no, porque no se borran.
- El plan de esta ronda está en `~/.claude/plans/eventual-baking-parrot.md`.
- **Correr todo**: `dotnet build -warnaserror` y después `dotnet run --no-build` en `tests\MetroCarpinteria.SmokeTest`.
- **Abrir la app**: `src\MetroCarpinteria.App\bin\Debug\net8.0-windows\MetroCarpinteria.exe`. Cerrarla antes de recompilar o el build falla por archivo bloqueado.
- El plan completo original está en `~/.claude/plans/fijate-esto-me-dijo-jaunty-penguin.md`.
