# Metro Carpintería

App de escritorio (WPF / .NET 8 + EF Core + SQLite) para un taller de carpintería.

## Antes de empezar

**Leé `docs/estado-del-trabajo.md`.** Tiene qué se hizo, qué falta, las decisiones de producto que no se deducen del código, y lo que ya se descartó para no volver a proponerlo.

## Lo que hay que saber sí o sí

- **Hay un usuario real en producción**: el dueño del taller, en la notebook del taller. Cada tag `vX.Y.Z` **se instala solo** en esa máquina la próxima vez que abre la app (Velopack + GitHub Releases). Empujar un tag es desplegar a la producción de otra persona: **confirmar siempre antes**.
- **Los números primero.** Un bug de pantalla molesta; uno de plata le hace perder confianza en todo lo demás.
- **Se corrige, no se borra.** Ningún borrado duro de plata: las correcciones compensan y los dos renglones quedan visibles.
- **Los importes se suman en memoria, nunca con `SUM()` de SQL.** Las columnas de dinero son `TEXT` a propósito; SQLite las pasaría por punto flotante y devolvería un número parecido y mal. Por eso el código llama a `.AsEnumerable()` antes del `.Sum()`. No es algo a optimizar.
- **WPF falla en silencio.** Un binding roto o una vista sin su trigger de opacidad no rompen nada visible y los tests pasan igual. Hay dos tests que los cazan; si tocás una vista, corré la suite y **abrí la app**.

## Cómo correr

```
dotnet build -warnaserror
cd tests\MetroCarpinteria.SmokeTest && dotnet run --no-build
```

La app: `src\MetroCarpinteria.App\bin\Debug\net8.0-windows\MetroCarpinteria.exe`. Cerrala antes de recompilar o el build falla por archivo bloqueado.
