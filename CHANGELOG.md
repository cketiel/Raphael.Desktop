# Changelog

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
The record starts at version `1.2.3`; earlier history is not reconstructed.

## [1.5.1] - 2026-09-02

### Changed
- The scheduled-ride SMS now asks the patient to reply **CONFIRM or DECLINE** instead of
  **CONFIRM or CANCEL**. The carrier's content filter blocks messages that contain the word
  CANCEL, so the confirmation request was never reaching the patient. Whoever reads the replies
  — the telephony provider and the customer service bot — has to accept the new keyword.

## [1.5.0] - 2026-08-29

### Added
- An **Admin → Google Maps** panel for administrators: what Google Maps costs, what the cache
  saves, and the settings that move both. Summary carries the period's figures — bought, cached,
  hit rate, estimated cost and the cost avoided — with a daily chart of bought against cached and
  the trend of the hit rate. Detail carries the breakdown per Google product, lifetime totals with
  no date filter, and CSV export. Settings holds the traffic mode, the cache retention, the margin
  and Google's price bands.
- Travel times, distances, addresses and road shapes now come from Raphael.Api instead of from
  Google directly, so one dispatcher's answer serves the next dispatcher and the driver on the
  same route.
- Help topics for every tab in Admin, and one per sub-tab of Google Maps.

### Changed
- **This application no longer holds a Google key for routing.** Everything a screen needs goes
  through the API, which serves what it already knows and buys only what nobody has asked for yet.
  The browser key that remains is used solely to draw the map.
- Route recalculation on the Schedules screen no longer asks Google once per stop. Marking a stop
  as performed re-chains the arrival times with no request at all, and moving a stop asks only
  about the legs that actually changed.
- The map pages moved off two Google classes that are no longer served to new Cloud projects:
  address autocomplete and the route drawn on the map. The route now comes from the API with the
  rest of the leg, so it is cached like everything else instead of being bought on every redraw.
- Map pages are served from a virtual host rather than from a string or a temporary file, which is
  what allows the browser key to be restricted to them.
- Travel times are priced against the hour the trip actually leaves, not the moment a dispatcher
  happens to open it.

### Fixed
- Distances and durations are no longer parsed from Google's localised text. A distance of
  `12.3 mi` read as `123` on a machine with Spanish regional settings.
- The trip form bought three traffic estimates whenever it opened a trip and displayed none of
  them.
- **F1 in Admin** opened the same page whichever tab was in front. It now opens the topic for the
  tab, and for the sub-tab inside Google Maps.

## [1.4.0] - 2026-08-28

### Added
- Help inside the application. **F1** opens the topic for the screen in front of you, resolving the
  deepest declaration above the focus: a dialog opens its own topic, not the one for the tab behind
  it. A `?` button sits in the top bar, and smaller ones on the Notification Centre toolbar and the
  administration header open the fine topic for that section.
- The help panel opens in its own window and docks back into a tab, the same way the Notification
  Centre does, keeping the page and scroll position across the move. The window remembers its
  position, size and state, and validates them against the monitors currently attached.
- Notifications documented in full, in Spanish and English: the bell and its two marks, live alerts
  and their durations, sound and silence, the Notification Centre, the notice details, Will Call,
  the catalogue of every notice and who receives it, read state, keeping and cleanup,
  administration, export, and what to check when nothing arrives. The other tabs ship an
  introduction and say so on the page.
- A shared NEMT glossary — Will Call, no-show, route, leg, space type, funding source — linked
  automatically into the running text, with the definition on hover.
- Offline search over the whole bundle, reached with `/`, which understands the words the office
  actually uses rather than only the ones on screen.
- "Copy report for support" on every page: version, build, role, language, topic and channel status
  in one paste.
- "Open it for me" links that carry out the action in the application instead of describing the
  route to it.
- Gear menu: **Help** and **About Raphael**, the latter showing the application version, the build
  date, and which version of the application the shipped help was written against.
- A What's new page, and a "New in X.Y.Z" badge on topics for the two versions after they arrive.

### Changed
- The help follows the application's language switch immediately, without restarting, and every
  page states which version of Raphael Desktop it covers and when it was last updated. If the
  application is ahead of the help, the page says so in a banner rather than describing a product
  that has moved on.

### Fixed
- Routing a trip could be refused with a bare "400 (Bad Request)". The Pull-in hour was being built
  from the pickup-to-dropoff leg rather than from the drive back to the garage that the hour
  actually describes, so on a long trip it ran past midnight and the server rejected the whole
  routing. Both routing paths — the Schedule tab and reassigning a run from Trips — now measure
  that leg and send it. Merged before this release but after 1.3.0, so it reaches dispatchers here.
- A refused routing now shows the reason the server gave. The response body was being discarded, so
  the dispatcher saw the status code and nothing else while the explanation was there all along.

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
- Confirming now shows a Confirmed mark where the deadline countdown was, in the inbox list as
  well as in the notice itself, rather than reporting itself only by a button greying out. Having
  to open a notice to find out it was already dealt with is what leads to confirming it twice, and
  every confirmation of a Will Call promises the patient a vehicle again.

## [1.2.3] - 2026-08-20

### Changed
- Users with the "Driver" role are restricted to the production report only, and further limited
  to seeing their own data.
