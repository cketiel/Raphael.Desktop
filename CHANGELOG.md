# Changelog

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
The record starts at version `1.2.3`; earlier history is not reconstructed.

## [1.8.0] - 2026-09-10

⚠️ Needs Raphael.Backend deployed with the `EarlyArrivalWait` migration applied. The waiting time
is derived by the server; against an older API the figure is simply absent and the rest of the tab
works as before.

### Added
- The waiting time the early-arrival rule creates at a pickup is now worked out, stored and shown.
  A vehicle is never displayed arriving more than a quarter of an hour before the hour the patient
  was promised - five minutes on a return - and what is not shortened is the drive, so the
  difference is a driver sitting at a door with the engine off. That figure used to be computed to
  raise the ETA and then discarded. It now appears as a chip against the arrival hour, as an
  optional `Wait` column, and as an amber row past a threshold an administrator sets
  (`Scheduling.EarlyArrivalWaitHighlightMinutes`, 30 by default, API only for now).
- A timeline view of the route: each stop drawn as a bar to scale - the drive, the wait, and the
  margin the rule keeps in front of the promised hour - with every hour marked and a full breakdown
  on hover. Switchable per row or for the whole grid.
- Reordering stops into a less efficient order is reported instead of passing unremarked: a
  non-blocking notice names the stops that ended up out of hour, and the order is saved as left.
  The dispatcher has the last word.
- Will Call now reaches every open Schedule tab live, both directions - a trip turned into a Will
  Call and a Will Call activated. Until now only the dispatcher who pressed the button saw it.

### Changed
- The garage departure hour and the waiting time at each pickup are derived by the server on every
  change of a route's shape, rather than trusted to whichever client happened to work them out.
  Paths that never computed them - reassigning a run from the Trips tab, for one - no longer leave
  the hour describing a route that has ceased to exist.
- A measured travel time is now filed against the hour the vehicle leaves as well as the two points
  it runs between, which is how the server files a leg it bought.

### Fixed
- The garage departure hour was recalculated on screen and never saved. The decision to write it
  compared the new hour against the row it had just been written into, so a save that never
  happened could never be retried: the dispatcher read the right departure all day and the driver
  was handed the wrong one.
- A trip's dropoff could be dragged in front of its own pickup, and the garage events could be
  dragged out of position. The check for both existed but never ran: the grid never bound the
  view model as its drag handler, so the whole of `IDragSource` was dead code.
- Travel times were bought for the hour the route had *before* the change that moved them, and the
  client treated a leg as measured for ever once it had been measured once, whatever hour the
  vehicle now left at. Routing a nine o'clock patient onto a route that used to start at eleven
  priced the drive in eleven o'clock traffic - and that figure is what the garage hour is derived
  from, so the error did not stay in the traffic estimate.
- Routing a trip in front of the whole route wrote an arrival hour chained off the garage hour the
  route had before it went in front, leaving the row wrong until some later recalculation happened
  to correct it.
- The Schedule tab ignored the language switch. Its labels were read once when the tab opened and
  never again - it is the only screen not built on `BaseViewModel`, which is where every other tab
  gets that subscription - so half of it stayed in the language loaded at start-up while anything
  built later came out in the current one.

## [1.7.0] - 2026-09-09

### Added
- The Home tab is redesigned end to end: a single TripId search box, a side filter panel with
  chips and savable views, one date field with a range and four presets, click to view / double
  click to edit, column choice, density, and a purple divider that totals the money for what is
  filtered while the bottom bar counts trips, cancellations and unrouted ones. Export to Excel
  writes exactly what is on screen.
- The map on Home now loads only when it is needed - selecting a trip, starting one, or asking
  for it - instead of on every tab open, and shows a drawn plan with the same pin colors and
  route arrow as the live map while it waits. The chosen map type (satellite, labels) and the
  app's language now travel with every map load and are remembered between trips.
- The trip-import screen is rewritten. Choosing a file no longer starts the import: the file is
  read and checked first, and a preflight pass catches four kinds of row that cannot be sent -
  missing TripId, a patient nobody could identify, a missing address, an address that is too
  long - before you press Import. While it imports, six figures fill in live, one honest overall
  bar tracks every request that will be made, and a collapsible detail panel shows a bar per
  request plus a running log that stays on screen as evidence once the import finishes.
- Rejected rows get a repair bench: each one shows what is wrong, what to change, the trip
  pre-filled with the field at fault outlined in red, and a retry button that only lights up once
  the correction actually answers the problem. Fixed rows can be retried together in one request.
  A duplicate now names the existing trip it collided with, so the fix is a TripId, not a guess.
- What is left after a run can be exported as a CSV in the exact format of the file you imported,
  named after the payer, the date and the words *not-imported*.
- Help topics [Home](topic:desktop/home/overview) and
  [Importing a broker's trips](topic:desktop/home/import-trips) rewritten to match.

### Fixed
- The pickup pin on the map plate was cropped at the edge of the card.
- Pressing F1 on the import screen opened the Home help topic instead of the import one - the
  screen's help topic now follows which mode Home is in.

## [1.6.1] - 2026-09-04

### Fixed
- Importing a broker's CSV file no longer floods the server. It opened five to ten threads and made
  up to six requests per row — two geocodings, a patient lookup, a patient insert, the trip and the
  history row — so four hundred trips came to roughly two thousand four hundred requests. The shared
  host reads a burst like that as an attack and withdraws the application's permissions: the
  connection dropped mid-file and trips went missing. The same file now costs about six requests,
  sent one at a time. Measured in production: a hundred and forty-eight trips imported in three.
- The Select CSV button was re-enabled the moment the file finished reading, before the import had
  begun, so a second file could be started on top of the first.
- A patient named on two rows of the same file could be created twice by two threads racing each
  other. The old code caught the failures and retried them at the end; now the race cannot happen.

### Changed
- The import preview shows the status and the reason for every row, so a rejected trip can be found
  in the original file by the broker's TripId and corrected. It used to list only what had gone in.
- Re-importing a file is safe and is now the documented fix for an interrupted import: trips are
  matched on the broker's TripId, so one that already went in is updated, never duplicated. A trip
  cancelled in the office stays cancelled when it appears again in the file.
- Every address in a file is resolved in one batched request to `POST /api/routing/geocode/batch`,
  which has existed since 1.5.0. A file that carries its own coordinates asks for none at all.

### Added
- Help topic *Importing a broker's trips* (`desktop/home/import-trips`), with the reason for each
  kind of rejection and what to correct in the file.

## [1.6.0] - 2026-09-03

### Added
- The Schedule tab is live. What one dispatcher does appears on the others' screens without a
  reload: a trip routed leaves the backlog, a trip taken off a route returns to it, and a route
  that is reordered reloads with its new ETAs. It never reloads while a dispatcher is mid-drag or
  mid-save. New `/hubs/dispatch` channel, which stores nothing and reaches no inbox — see
  `_meta/REALTIME_POLICY.md`.
- The driver's Arrive and Perform flags appear as they happen, from business events that were
  already being published and simply were not being listened to on this screen.
- The vehicle's position is pushed from the driver's own report instead of polled every five
  seconds, and is interpolated over the real gap between two reports. The marker is a heading
  arrow that turns by the shorter way round and holds its heading when the vehicle stops.
- The header of the trips grid's first column counts what is left: unrouted trips, and how many of
  them are Will Calls.
- `PUT /api/schedules/resequence` writes a whole route's order in one request.

### Changed
- The unrouted trips grid virtualises again, on all three axes. It had been switched off, so three
  to four hundred rows of twenty-four columns were built before anything appeared.
- The action buttons in each row work on the first click, and the selected row is marked with an
  accent bar down its left edge.
- Collections are refilled in one notification instead of one per row; map markers are placed in
  one pass instead of fourteen coordinate conversions per marker per gesture; and reordering a
  route no longer reloads the day's trips.
- One shared connection pool for the whole application instead of one HttpClient per service.
- The scheduled-ride SMS asks the patient to press 1 to confirm or 9 to decline, instead of
  replying with a word.

### Fixed
- Routing the first trip of a day renumbered the garage stops of every other day on that route —
  in practice, reordering the route of a driver who might be on the road.
- One row in every two never showed the late-ETA warning, the selection, or any state colour.
  `AlternatingRowBackground` is applied by coercion, which outranks both setters and triggers.
- The late-ETA warning was also hidden on any row that was selected or under the pointer, because
  the selected cells paint over the row.
- The vehicle marker pointed the wrong way: a car drawn in profile cannot be rotated by a compass
  heading. A stopped vehicle no longer swings its heading to north.
- `GET /api/schedules/unscheduled` no longer returns cancelled trips for the client to discard.

### Security
- The driver's password hash and push token were being serialised inside `GET /api/Runs`, which
  returns route entities whole. Both are cut from serialisation on the entity.
- `EnableSensitiveDataLogging` was on in production, writing patient names, addresses and phone
  numbers into the logs. Development only now.

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
