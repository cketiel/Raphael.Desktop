using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.Help;
using Raphael.Desktop.Services.Maps;
using Raphael.Desktop.ViewModels.LiveMap;

namespace Raphael.Desktop.Views.LiveMap
{
    /// <summary>
    /// RE-086: the rest of the voice assistant's tools — everything a dispatcher does on this screen
    /// with the mouse, plus the help. Each one presses what the screen itself presses.
    /// </summary>
    /// <remarks>
    /// ⚠️ HIPAA: every result here goes to OpenAI. Nothing in it may carry a patient's name, phone or
    /// address: the next stop goes as its kind and times, the trips in progress as their Trip Id.
    /// </remarks>
    public partial class LiveMapView
    {
        private static object Ok() => new { ok = true };
        private static object Fail(string why) => new { ok = false, error = why };

        private LiveMapVehicleViewModel? FindLine(string query)
        {
            var q = query.Trim();
            if (q.Length == 0) return null;
            bool Has(string? f) => !string.IsNullOrEmpty(f) && f.Contains(q, StringComparison.OrdinalIgnoreCase);
            return ViewModel.Vehicles.FirstOrDefault(v => string.Equals(v.Name, q, StringComparison.OrdinalIgnoreCase))
                ?? ViewModel.Vehicles.FirstOrDefault(v => Has(v.Name) || Has(v.DriverName) || Has(v.VehicleName)
                    || (_plates.TryGetValue(v.VehicleRouteId, out var plate) && Has(plate)));
        }

        /// <summary>Null when the tool is not one of these.</summary>
        private async Task<object?> RunScreenToolAsync(string name, JsonElement a)
        {
            string Arg(string n) => a.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            bool Flag(string n) => a.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
            const string noLine = "No line is selected: select one first";
            var selected = ViewModel.Selected;

            switch (name)
            {
                case "open_search":
                    if (!LiveMapVoiceTools.SearchBoxes.Contains(Arg("box"))) return Fail("Unknown box");
                    Post(new { type = "openSearch", box = Arg("box") });
                    return Ok();

                case "pan_map":
                    Post(new { type = "pan", direction = Arg("direction") });
                    return Ok();

                case "street_view":
                    Post(new { type = "streetViewPick", on = Flag("on") });
                    return Flag("on") ? new { ok = true, next = "The dispatcher must now click a street on the map; Esc cancels" } : Ok();

                case "window":
                    switch (Arg("action"))
                    {
                        case "hide_panels": Post(new { type = "setClean", on = true }); return Ok();
                        case "show_panels": Post(new { type = "setClean", on = false }); return Ok();
                        case "reload": await ViewModel.ReloadCommand.ExecuteAsync(null); return Ok();
                        case "full_screen": case "own_window":
                            // Moving to another window reloads the page, and the voice session with it: answer first.
                            var full = Arg("action") == "full_screen";
                            _ = Dispatcher.BeginInvoke(async () =>
                            {
                                await Task.Delay(2500);
                                if (full) ToggleFullScreen(); else ViewModel.ToggleWindowCommand.Execute(null);
                            });
                            return new { ok = true, note = "It moves in a moment; the map reloads and the voice session ends, so say it before" };
                        default: return Fail("Unknown action");
                    }

                case "follow_vehicle":
                    if (selected is null) return Fail(noLine);
                    ViewModel.IsFollowing = Flag("on");
                    return new { ok = true, following = ViewModel.IsFollowing, line = selected.Name };

                case "set_road_view":
                    if (selected is null) return Fail(noLine);
                    ViewModel.ShowWholeDay = Arg("view") == "day";
                    return new { ok = true, view = Arg("view"), line = selected.Name };

                case "details_card":
                    if (!Flag("open")) { ViewModel.RouteCard.IsOpen = false; return Ok(); }
                    if (selected is null) return Fail(noLine);
                    await ViewModel.RouteCard.OpenAsync(selected);
                    return new { ok = true, line = selected.Name };

                case "card_link":
                {
                    if (selected is null) return Fail(noLine);
                    var card = ViewModel.RouteCard;
                    if (card.Vehicle != selected || card.Detail is null) await card.OpenAsync(selected);
                    if (card.Detail is null) return Fail("The line could not be read");
                    switch (Arg("link"))
                    {
                        case "route_in_dispatch": card.OpenRouteCommand.Execute(null); break;
                        case "trip_in_home":
                            if (!card.HasCurrentTrip) return Fail("The line has no trip in progress");
                            card.OpenTripCommand.Execute(null); break;
                        case "vehicle_documents": card.OpenVehicleDocsCommand.Execute(null); break;
                        case "driver_documents":
                            if (card.Detail.DriverId is null) return Fail("The line has no driver");
                            card.OpenDriverDocsCommand.Execute(null); break;
                        default: return Fail("Unknown link");
                    }
                    return new { ok = true, opened = "in another tab" };
                }

                case "set_filter":
                {
                    var group = ViewModel.Groups.FirstOrDefault(g => string.Equals(g.Group, Arg("group"), StringComparison.OrdinalIgnoreCase));
                    if (group is null) return Fail("Unknown group");
                    group.IsOn = Flag("on");
                    return new { ok = true, group = group.Name, shown = group.IsOn, lines = group.Count };
                }

                case "set_layer":
                {
                    var on = Flag("on");
                    switch (Arg("layer"))
                    {
                        case "routes_in_progress": ViewModel.ShowRoutes = on; return new { ok = true, status = ViewModel.RoutesStatus };
                        case "google_traffic": ViewModel.ShowTraffic = on; return Ok();
                        // They bill the user's key and the screen says the cost before starting: never by voice.
                        case "delays_google" or "delays_tomtom" when on:
                            return Fail("This layer costs money on the user's key: the dispatcher must tick it in LAYERS, where the cost is stated first");
                        case "delays_google": ViewModel.ShowDelays = false; return Ok();
                        case "delays_tomtom": ViewModel.ShowDelaysTomTom = false; return Ok();
                        default: return Fail("Unknown layer");
                    }
                }

                case "get_map_state":
                    return new
                    {
                        ok = true,
                        live = ViewModel.IsLive,
                        filters = ViewModel.Groups.Select(g => new { group = g.Group, name = g.Name, shown = g.IsOn, lines = g.Count }),
                        layers = new { routes = ViewModel.ShowRoutes, googleTraffic = ViewModel.ShowTraffic, delaysGoogle = ViewModel.ShowDelays, delaysTomTom = ViewModel.ShowDelaysTomTom },
                        paceFrom = ViewModel.PaceSourceText,
                        selectedLine = selected?.Name,
                        roadView = ViewModel.ShowWholeDay ? "day" : "trip",
                        following = ViewModel.IsFollowing,
                        detailsOpen = ViewModel.RouteCard.IsOpen,
                        inProgressListOpen = ViewModel.IsInProgressOpen,
                        panelsHidden = _cleanView,
                        ownWindow = ViewModel.IsInOwnWindow,
                        fullScreen = _beforeFullScreen is not null
                    };

                case "get_line_status":
                {
                    var line = Arg("query").Trim().Length > 0 ? FindLine(Arg("query")) : selected;
                    if (line is null) return Fail(Arg("query").Length > 0 ? "No line matches" : noLine);
                    var d = await new LiveMapService().GetRouteDetailAsync(line.VehicleRouteId);
                    if (d is null) return Fail("The line could not be read");
                    return new
                    {
                        ok = true,
                        line = line.Name, driver = line.DriverName, vehicle = line.VehicleName, kind = d.Kind, state = line.GroupName,
                        speedMph = Math.Round(line.SpeedMph), moving = line.IsMoving, speeding = line.IsSpeeding,
                        lastPositionMinutesAgo = line.LastFixUtc is DateTime f ? (int?)Math.Round((DateTime.UtcNow - f).TotalMinutes) : null,
                        lateMinutes = line.LateMinutes,
                        tripsDone = d.TripsDone, tripsToday = d.TripsToday, tripsCancelled = d.TripsCanceled,
                        shift = line.HasShift ? line.ShiftText : null, miles = line.HasShift ? line.MilesText : null,
                        // Kind and times only: the name on the screen is the patient's.
                        nextStop = d.NextStop is null ? null : new { kind = d.NextStop.Kind, promised = Hm(d.NextStop.Promised), eta = Hm(d.NextStop.Eta) },
                        tripInProgress = d.CurrentTrip is null ? null : new { tripId = d.CurrentTrip.TripNumber ?? d.CurrentTrip.TripId.ToString(), status = d.CurrentTrip.Status },
                        arrival = d.Progress is null ? null : new { heading = d.Progress.Phase, milesLeft = d.Progress.RemainingMiles, pickupEta = Hm(d.Progress.PickupEta), dropoffEta = Hm(d.Progress.DropoffEta) },
                        vehicleDocumentsExpired = d.VehicleCompliance?.Expired, driverDocumentsExpired = d.DriverCompliance?.Expired
                    };
                }

                case "get_day_status":
                {
                    int N(TripPart p) => ViewModel.Day.Rows.FirstOrDefault(r => r.Part == p)?.Count ?? 0;
                    return new
                    {
                        ok = true,
                        trips = ViewModel.Day.Rows.Sum(r => r.Count), completed = N(TripPart.Done), inProgress = N(TripPart.InProgress),
                        notStarted = N(TripPart.NotStarted), cancelled = N(TripPart.Canceled), withoutRun = N(TripPart.NoRun), withoutRunWillCall = ViewModel.Day.WillCallCount, leftToRoute = ViewModel.Day.ToRouteCount, impossibleTrips = ViewModel.Day.ImpossibleCount, impossibleHours = ViewModel.Day.ImpossibleDetail, looseTrips = ViewModel.Day.LooseCount, looseHours = ViewModel.Day.LooseDetail, done = ViewModel.Day.DoneText
                    };
                }

                case "get_trips_in_progress":
                {
                    var trips = await new LiveMapService().GetInProgressAsync();
                    var lines = ViewModel.Vehicles.ToDictionary(v => v.VehicleRouteId);
                    return new
                    {
                        ok = true,
                        count = trips.Count,
                        trips = trips.Take(30).Select(t =>
                        {
                            var v = t.VehicleRouteId is int id && lines.TryGetValue(id, out var x) ? x : null;
                            return new
                            {
                                tripId = string.IsNullOrWhiteSpace(t.TripNumber) ? t.TripId.ToString() : t.TripNumber,
                                line = v?.Name, driver = v?.DriverName, space = t.SpaceType,
                                phase = t.Status switch { "InProgress" => "on board", "Arrived" => "at the door", _ => "on the way to the pickup" },
                                promised = Hm(t.Status == "InProgress" ? t.ToTime : t.FromTime), lateMinutes = v?.LateMinutes
                            };
                        }).ToList()
                    };
                }

                case "open_in_progress_list":
                    if (ViewModel.IsInProgressOpen != Flag("open"))
                        if (Flag("open")) ViewModel.Day.AskInProgress(); else ViewModel.IsInProgressOpen = false;
                    return Ok();

                case "open_day_part":
                {
                    TripPart? part = Arg("part") switch
                    {
                        "completed" => TripPart.Done, "not_started" => TripPart.NotStarted,
                        "cancelled" => TripPart.Canceled, "without_run" => TripPart.NoRun, _ => null
                    };
                    if (part is null) return Fail("Unknown part");
                    ViewModel.Day.OpenPart(part.Value);
                    return new { ok = true, opened = part == TripPart.NoRun ? "Dispatch, in another tab" : "Home, in another tab" };
                }

                case "search_help":
                {
                    var lang = LocalizationService.Instance.CurrentLanguage;
                    var ids = Arg("scope") == "all"
                        ? HelpTopicIds(lang)
                        : HelpReader.Topics(LiveMapVoiceTools.HelpFolder, lang);
                    var found = HelpReader.Search(Arg("question"), ids, lang);
                    return found.Count == 0
                        ? Fail("The help has nothing about that")
                        : new { ok = true, sections = found.Select(x => new { topic = x.Topic.Id, title = x.Topic.Title, heading = x.Section.Heading, text = HelpReader.Cut(x.Section.Text).Length > 1800 ? x.Section.Text[..1800] + "…" : x.Section.Text }).ToList() };
                }

                case "read_help":
                {
                    var t = HelpReader.Read($"{LiveMapVoiceTools.HelpFolder}/{Arg("topic")}", LocalizationService.Instance.CurrentLanguage);
                    return t is null
                        ? Fail("Unknown topic: " + string.Join(", ", HelpReader.Topics(LiveMapVoiceTools.HelpFolder, LocalizationService.Instance.CurrentLanguage).Select(x => x.Split('/').Last())))
                        : new { ok = true, title = t.Title, text = HelpReader.Cut(string.Join("\n\n", t.Sections.Select(s => $"## {s.Heading}\n{s.Text}"))) };
                }

                case "open_help":
                    OpenHelp(Arg("topic"));
                    return Ok();
            }
            return null;
        }

        private static List<string> HelpTopicIds(string lang) =>
            System.IO.Directory.Exists(System.IO.Path.Combine(HelpService.BundleRoot, lang))
                ? System.IO.Directory.GetFiles(System.IO.Path.Combine(HelpService.BundleRoot, lang), "*.html", System.IO.SearchOption.AllDirectories)
                    .Select(f => System.IO.Path.GetRelativePath(System.IO.Path.Combine(HelpService.BundleRoot, lang), f).Replace('\\', '/')[..^5])
                    // What's new names everything and explains nothing.
                    .Where(id => !id.Contains("whats-new")).ToList()
                : new List<string>();

        /// <summary>Opens the help of this screen: F1 inside the page (which WPF never sees) and the assistant.</summary>
        /// <summary>
        /// F1: the help of what is under the mouse, when that is this map. Over the page itself the page
        /// knows which part it is, and answers with a "help" message.
        /// </summary>
        public static bool OpenHelpUnderMouse()
        {
            var over = System.Windows.Input.Mouse.DirectlyOver as DependencyObject;
            var view = over;
            while (view is not null and not LiveMapView) view = VisualTreeHelper.GetParent(view);
            if (view is not LiveMapView map) return false;

            var topic = HelpAssist.ResolveUpwards(over!);
            if (topic is null || topic == HelpFolderOverview)
            {
                if (!map._pageReady) { OpenHelp(null); return true; }
                map.Post(new { type = "helpHere" });
            }
            else HelpService.Instance.Open(topic);
            return true;
        }

        private const string HelpFolderOverview = LiveMapVoiceTools.HelpFolder + "/overview";

        private static void OpenHelp(string? topic) =>
            HelpService.Instance.Open($"{LiveMapVoiceTools.HelpFolder}/{(string.IsNullOrWhiteSpace(topic) ? "overview" : topic.Trim())}");

        private static string? Hm(TimeSpan? t) => t?.ToString(@"hh\:mm");
    }
}
