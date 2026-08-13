# METRO CARPINTERÍA

Aplicación de escritorio Windows para gestión de carpintería.

## Requisitos

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download) (solo para desarrollo)

## Ejecutar en desarrollo

```bash
dotnet run --project src/MetroCarpinteria.App
```

## Publicar una versión nueva

Basta con etiquetar el commit y empujarlo:

```bash
git tag v1.1.0 && git push origin v1.1.0
```

GitHub Actions compila, arma el instalador y publica el release solo. El proceso
está en [`.github/workflows/release.yml`](.github/workflows/release.yml) y no usa
tu conexión: todo corre en los servidores de GitHub.

La versión sale del tag, así que no hay que editar el `.csproj` en cada publicación.

## Instalar en la notebook del taller

Bajar el `MetroCarpinteria-win-Setup.exe` de la
[última versión](https://github.com/lovera2025/Carpinteria_App/releases/latest)
y ejecutarlo. Se instala para el usuario actual, sin pedir permisos de
administrador, y deja el acceso directo en el menú inicio.

La primera vez Windows puede mostrar *"Windows protegió tu PC"* porque el
instalador no está firmado digitalmente: entrar en **Más información → Ejecutar
de todos modos**.

**Se hace una sola vez.** De ahí en adelante la app se actualiza sola: al abrir
busca si hay una versión nueva, la descarga en segundo plano y la instala cuando
la cerrás. Velopack baja solo las diferencias, así que una actualización suele
pesar pocos megas y no el paquete completo.

Sin internet la aplicación funciona igual: el chequeo falla en silencio.

El comportamiento se configura en **Configuración → Actualizaciones**, donde
también se puede buscar una actualización a mano.

## Módulos

- **Inventario** — productos, stock, precio de costo, alertas, movimientos
- **Caja** — apertura, ingresos/egresos, cierre
- **Presupuestos** — cotización con materiales, mano de obra por persona, fotos de referencia, calculadora, vigencia, impresión y PDF
- **Proyectos** — trabajos, materiales (descuenta stock), estados
- **Personal** — empleados con su jornal y asignación a proyectos
- **Reportes** — resumen de inventario, caja y proyectos
- **Configuración** — respaldos y rutas de datos
- **Acerca de** — marca Metro Carpintería

## Presupuestos

Un presupuesto es un proyecto en estado *Presupuesto*: el mismo registro acompaña
al trabajo desde la cotización hasta la entrega.

**Armar el presupuesto no toca el inventario.** Los materiales se cargan del catálogo
(trae nombre, unidad y precio de costo) o sueltos, con la opción de guardarlos en el
inventario con stock 0. Si no alcanza el stock, avisa pero deja cotizar igual.

**El precio se congela.** Se guardan las entradas del cálculo —cantidades, precios
unitarios, días, jornal y los cuatro porcentajes—, así un presupuesto ya entregado
sigue dando lo mismo aunque después cambien los precios o el margen del taller.

### Fórmula

```
desperdicio          = materiales × 16 %
desgasteHerramientas = materiales ×  9 %
jornalJefe           = valorDia × díasJefe
jornalOperarios      = Σ (jornalOperario × díasOperario)
manoDeObra           = jornalJefe + jornalOperarios
gastosAdicionales    = jornalJefe × 50 %
ganancia             = jornalJefe × 30 %
precioFinal          = suma de los seis
```

Los porcentajes se editan en *Ajustes avanzados* y se pueden guardar como valores
por defecto. Cada concepto se redondea por separado para que el desglose sume
exactamente el precio final.

### Mano de obra

El jefe son los campos *Días del jefe* y *Jornal del jefe*. Los operarios se agregan
de a uno en la tarjeta **Operarios**, cada uno con sus días y su jornal: no todos
cobran lo mismo ni están la misma cantidad de días.

Los operarios se eligen de **Personal**, y el jornal viene de la ficha —se carga una
sola vez ahí— pero se puede pisar para ese presupuesto sin tocarle el legajo a nadie.
También se puede escribir un nombre a mano para alguien que no está dado de alta.

Gastos adicionales y ganancia se calculan solo sobre el jornal del jefe. Los
operarios se suman al costo, sin ese 50 % ni ese 30 % encima.

**Al aprobar, los operarios cotizados quedan asignados al proyecto solos.** Los que se
escribieron a mano no, porque no hay ficha a la que engancharlos.

Un presupuesto sin operarios es «lo hace el jefe solo» y da exactamente el mismo
precio de siempre: los presupuestos anteriores a esto no se movieron ni un peso.

### Vigencia

Se autocompleta a 15 días. El estado —vigente, por vencer, vencido— se calcula con
la fecha y **nunca cambia nada por su cuenta**: un presupuesto vencido sigue en la
lista y se puede aprobar igual. Inicio muestra cuántos están esperando respuesta.

### Aprobar

Pasa el trabajo a *En curso* y descuenta del inventario el stock disponible de cada
material cotizado. Si falta, descuenta lo que hay y deja la lista de lo que falta
comprar; el botón **Descontar pendientes** salda el resto cuando llega el material.

### Imprimir y guardar en PDF

Dos documentos distintos, cada uno con su botón de imprimir y su botón de **Guardar
PDF**. El PDF pregunta dónde guardarlo, con el nombre ya puesto
—`Presupuesto 0042 - Juan Perez.pdf`— y lo abre al terminar, listo para mandar.

- **Presupuesto para el cliente** — el trabajo, la descripción, las fotos de
  referencia y el TOTAL. Un solo número: sin lista de materiales y sin desglose.
- **Hoja de costos** — uso interno, con desperdicio, desgaste, gastos, ganancia,
  margen efectivo y la mano de obra persona por persona.

En la hoja de costos, la columna **Pesa en el precio** muestra cuánto le suma cada
persona al presupuesto. Gastos y ganancia van solo sobre el jornal del jefe; un
ayudante de $ 22.000 por día durante tres días cobra $ 66.000 y pesa exactamente
eso. Es el número que sirve para decidir a quién poner en un trabajo, y por eso
nunca sale del taller.

El PDF se arma sin librerías externas: cada hoja se dibuja tal como saldría impresa.
El texto no se puede seleccionar ni buscar —es una imagen de la hoja—, pero se ve e
imprime igual que el papel, que es para lo que se usa.

Los dos documentos entran en **una sola hoja A4**. La hoja de costos completa lo hace
con unos 30 píxeles de sobra, así que hay que medirla al tocar el diseño:

```bash
dotnet run --project tests/MetroCarpinteria.SmokeTest -- --documents artifacts/quote-preview
```

## Datos locales

```
Documentos/MetroCarpinteria/
  data/carpinteria.db
  data/quote-images/
  backups/
  settings.json
```

Los datos permanecen en la PC. No se requiere internet para operar.

## Créditos

**Metro Carpintería** — Diseños a medida | 3777-412207  
Desarrollado por L.M · 2026
