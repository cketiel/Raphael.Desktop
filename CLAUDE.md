# Raphael.Desktop — WPF (.NET 8)

Dispatch / back-office. Parte del ecosistema Raphael (NEMT). Reglas globales: `../CLAUDE.md`.

## Rol
Herramienta del dispatcher: gestiona customers, rutas, billing, funding sources, reportes de
producción y administración de usuarios/roles. Es la app con más superficie del ecosistema —
si un DTO llega incompleto aquí, el dispatcher toma decisiones de ruta con datos parciales.

Target: `net8.0-windows`

## Contrato con la API
- Auth: **JWT** → `Services/AuthService.cs`
- Cliente HTTP: `Services/ApiClientFactory.cs`
- Config: `appsettings.json`
- DTOs espejo: `DTOs/` (25) — **copias manuales** de `Raphael.Backend/Raphael.Shared/DTOs/`

⚠️ **Drift abierto (menor):** `DTOs/ScheduleDto.cs` tiene **33 propiedades**, el backend expone **36**.
Faltan `CustomerId, CustomerPhone, VehicleRouteId`. Ninguna pantalla las pinta hoy.
Hasta RE-008 aquí ponía que faltaban 8: cinco de ellas ya estaban. Ver `../_meta/CONTRACT_MAP.md`.

⚠️ **La pestaña Schedule tiene doctrina propia:** `../_meta/REALTIME_POLICY.md`. Su canal en vivo
(`/hubs/dispatch`) **no es el de notificaciones** y no persiste nada. Antes de mandar algo por ahí,
o de convertirlo en notificación, lee §3 de ese documento.

`DTOs/ProblemDetails.cs` es local (envelope RFC 7807), no tiene ni necesita contraparte en backend.

## Anclas
- Entrada: `App.xaml.cs` → `MainWindow.xaml`
- HTTP/dominio: `Services/` (un servicio + interfaz por entidad: `CustomerService`/`ICustomerService`, etc.)
- Estado: `ViewModels/` (patrón `BaseViewModel` + `AddEdit*ViewModel` por entidad) · UI: `Views/`
- Mapeo DTO↔Model: `Mappers/`

## Convenciones no obvias
- MVVM con `Commands/` explícitos (no code-behind con lógica); un ViewModel por vista o por
  operación CRUD (`AddEditXViewModel`, `XPopupViewModel`).
- Un servicio siempre va con su interfaz (`IXService` + `XService`) para poder mockear en pruebas
  manuales — aunque hoy no hay proyecto de test automatizado.
- Roles y permisos condicionan la UI (ver hallazgo reciente: rol "Driver" restringido a su propio
  reporte de producción) — al tocar `AdminViewModel` o vistas de reportes, revisar el rol activo.
- PHI en pantallas de Customer/Trip: nunca a logs.

## No leer
`bin/`, `obj/`, `*.user` (`Meditrans.Client.csproj.user`), `*.ico`, `Assets/` binarios

## Comandos
- Build: `dotnet build Raphael.Desktop.sln`
- Run: `dotnet run --project Raphael.Desktop.csproj`
- Test: no hay proyecto de tests. Si el cambio toca billing o estados de viaje, dilo explícitamente.
