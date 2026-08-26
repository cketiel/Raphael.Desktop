# Changelog

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
The record starts at version `1.2.3`; earlier history is not reconstructed.

## [1.3.0] - 2026-08-25

### Added
- Notification Center for the dispatch office: one inbox for the whole application, read by both
  the bell badge and the panel, with tabs for unattended notices, Will Calls, cancellations and
  trip progress. Notices are grouped by trip, translated into the language the dispatcher picked,
  and can be archived so the cleanup never removes them.
- Live alerts that stack while the office works. Up to three cards at a time; the rest fold into
  a counter that opens the panel. A Will Call alert floats in a window that never takes focus, so
  it is visible even while the dispatcher is in the phone system.
- Will Call can now be activated from the office. `Trip.WillCall` moves through two endpoints and
  nowhere else, which means activating one finally tells the patient — until now the dispatcher
  edited the field by hand and nobody was notified.
- `Arrived` trip status, between `Started` and `InProgress`, for when the driver has reached the
  pickup address and is waiting for the passenger to board.

### Changed
- Times are shown on the clock of whoever is reading them, and the business hours of a trip come
  from its provider's timezone rather than from the machine the server runs on.
- The notification detail pane reads a trip's events through a dedicated endpoint instead of
  loading its route's whole day and keeping two rows. Trips that are unrouted, or whose events
  are filed under another date, now show their journey too.

### Fixed
- A trip that was cancelled elsewhere can no longer be scheduled by mistake. The row is marked,
  its schedule button is disabled and it leaves the open trips list, and the trip is re-checked
  before scheduling in case the alert never arrived.
- An icon hover animation could close the application. It applied a scale transform to icons that
  already carried a rotate transform, and the animation had no property to resolve.
- A Will Call notice showed an error instead of its message, and neither of its countdowns ever
  ran. The instant it carries was read with a combination of parsing options .NET rejects, so the
  read failed every time it was attempted.
- Confirming a Will Call left the Confirm button live once the inbox had been reloaded, so the
  same patient could be promised a vehicle twice. Rows now take on the reloaded payload instead
  of holding the one they were built from, which also keeps the archive state honest.
- Confirming now shows a Confirmed badge where the deadline countdown was, rather than reporting
  itself only by a button greying out.

## [1.2.3] - 2026-08-20

### Changed
- Users with the "Driver" role are restricted to the production report only, and further limited
  to seeing their own data.
