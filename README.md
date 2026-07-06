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

- **Inventario** — productos, stock, alertas, movimientos
- **Caja** — apertura, ingresos/egresos, cierre
- **Proyectos** — trabajos, materiales (descuenta stock), estados
- **Personal** — empleados y asignación a proyectos
- **Reportes** — resumen de inventario, caja y proyectos
- **Configuración** — respaldos y rutas de datos
- **Acerca de** — marca Metro Carpintería

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
