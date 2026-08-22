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
- DTOs espejo: `DTOs/` (23) — **copias manuales** de `Raphael.Backend/Raphael.Shared/DTOs/`

⚠️ **Drift abierto:** `DTOs/ScheduleDto.cs` tiene **28 propiedades**, el backend expone **36**.
Faltan `CustomerId, CustomerPhone, Distance, ETA, On, Sequence, Travel, VehicleRouteId`. Cualquier
pantalla que muestre schedule/ruta está ciega a esos campos aunque la API ya los devuelva. Antes de
construir sobre `ScheduleDto`, sanear el DTO. Ver `../_meta/CONTRACT_MAP.md`.

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
