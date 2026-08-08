# METRO CARPINTERÍA

Aplicación de escritorio Windows para gestión de carpintería.

## Requisitos

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download) (solo para desarrollo)

## Ejecutar en desarrollo

```bash
dotnet run --project src/MetroCarpinteria.App
```

## Crear instalador (.exe) para la notebook

```bash
dotnet publish src/MetroCarpinteria.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

El ejecutable queda en `publish/MetroCarpinteria.exe`. Copialo a la notebook del taller (no requiere instalar .NET por separado).

## Módulos

- **Inventario** — productos, stock, precio de costo, alertas, movimientos
- **Caja** — apertura, ingresos/egresos, cierre
- **Presupuestos** — cotización con materiales, calculadora, vigencia e impresión
- **Proyectos** — trabajos, materiales (descuenta stock), estados
- **Personal** — empleados y asignación a proyectos
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
manoDeObra           = valorDia × cantidadDias
gastosAdicionales    = manoDeObra × 50 %
ganancia             = manoDeObra × 30 %
precioFinal          = suma de los seis
```

Los porcentajes se editan en *Ajustes avanzados* y se pueden guardar como valores
por defecto. Cada concepto se redondea por separado para que el desglose sume
exactamente el precio final.

### Vigencia

Se autocompleta a 15 días. El estado —vigente, por vencer, vencido— se calcula con
la fecha y **nunca cambia nada por su cuenta**: un presupuesto vencido sigue en la
lista y se puede aprobar igual. Inicio muestra cuántos están esperando respuesta.

### Aprobar

Pasa el trabajo a *En curso* y descuenta del inventario el stock disponible de cada
material cotizado. Si falta, descuenta lo que hay y deja la lista de lo que falta
comprar; el botón **Descontar pendientes** salda el resto cuando llega el material.

### Impresión

Dos documentos distintos, y se imprimen con el diálogo de Windows (con *Microsoft
Print to PDF* sale el archivo):

- **Presupuesto para el cliente** — materiales, mano de obra y total. Sin los
  porcentajes internos.
- **Hoja de costos** — uso interno, con desperdicio, desgaste, gastos y ganancia.

## Datos locales

```
Documentos/MetroCarpinteria/
  data/carpinteria.db
  backups/
  settings.json
```

Los datos permanecen en la PC. No se requiere internet para operar.

## Créditos

**Metro Carpintería** — Diseños a medida | 3777-412207  
Desarrollado por L.M · 2026
