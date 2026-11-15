# Heartbeat Portal

Portal Blazor de Heartbeat para el control de misión.

> **Datos simulados.** La demo muestra datos ficticios para probar los flujos del
> portal. Heartbeat es una herramienta de apoyo a la decisión y no sustituye el
> criterio de profesionales cualificados.
>
> **No es un dispositivo médico ni diagnostica, previene o trata enfermedades.**
> No uses sus estados, alertas ni umbrales como consejo médico.

## Datos, fórmulas y umbrales

Los umbrales y fórmulas son configurables y se mantienen fuera de la UI, en el
catálogo compartido de indicadores:
[`indicators.catalog.json`](https://github.com/AliaJimenez/HeartbeatBackend/blob/dev/contracts/indicators.catalog.json).
Cada indicador documenta su `source` y el estado de la evidencia (`verified` o
`heuristic`). Los valores actuales son demo-grade y deben revisarse antes de
usarse fuera de una demostración.

## Requisitos

- .NET SDK 10.0.x

## Compilar, probar y ejecutar

```bash
dotnet build HeartbeatFrontWeb.sln
dotnet test HeartbeatFrontWeb.sln
dotnet run --project src/Heartbeat.Portal/Heartbeat.Portal.csproj
```

El detalle de un astronauta incluye un asistente Claude para usuarios con `vitals.view`. El portal envía la sesión de
Mission Control a Heartbeat.Api y nunca recibe ni almacena la API key de Anthropic. Claude solo explica el estado y las
alertas abiertas que Heartbeat ya calculó; no diagnostica ni sustituye procedimientos oficiales.

El backend, los contratos y todos los tickets viven en
[HeartbeatBackend](https://github.com/AliaJimenez/HeartbeatBackend). La app de
escritorio vive en
[HeartbeatFrontDesktop](https://github.com/IsaiasMorillo/HeartbeatFrontDesktop).
