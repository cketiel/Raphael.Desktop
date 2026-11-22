using System.Text.Json.Nodes;

namespace Raphael.Desktop.Services.Maps
{
    /// <summary>
    /// What the voice assistant may do on the live map, and how it is told to behave: the
    /// equivalent of God's Eye View's <c>instructions.js</c> and <c>GEV_REALTIME_TOOLS</c>.
    /// The model can only act through these tools; the page and this application run them.
    /// </summary>
    /// <remarks>
    /// ⚠️ HIPAA: tool results never carry patient names, phones or addresses — only Trip Ids, line
    /// names, drivers (staff) and states. What the dispatcher says aloud does reach OpenAI.
    /// </remarks>
    public static class LiveMapVoiceTools
    {
        public static readonly string[] Styles = { "normal", "retro", "surveillance", "thermal", "anime", "noir", "snow" };
        public static readonly string[] Sources = { "roadmap-nolabels", "roadmap", "hybrid", "satellite", "3d" };

        public const string Instructions =
            "You are Raphael Voice Control, a concise voice controller for the live map of Raphael's dispatch station (non-emergency medical transport).\n" +
            "Have a natural, short spoken conversation while the mic is on. Speak the language the dispatcher speaks to you (English or Spanish).\n" +
            "Only control the map by calling the provided tools. Never invent tool names or arguments. For ordinary questions, answer without tools.\n" +
            "A line is a vehicle route of the day; it has a name, a driver, a vehicle and a plate. \"Show me Aisha\", \"select LM-04\", \"where is the van with plate X\" mean select_line.\n" +
            "\"Where is trip 278\", \"find trip ABC123\", \"which vehicle has trip 278\" mean find_trip with the Trip Id.\n" +
            "\"Go to Cape Coral\", \"take me to the hospital on Cleveland Avenue\" mean fly_to_place.\n" +
            "\"Zoom in\", \"closer\", \"zoom out a lot\" mean zoom_map. \"Show the whole fleet\", \"fit everything\" mean fit_fleet. \"Deselect\", \"clear\" mean clear_selection.\n" +
            "\"How many vehicles are on route\", \"who is out of service\" mean get_fleet_status, then answer from its result.\n" +
            "Visual styles: normal, retro (CRT), surveillance (NVG / night vision), thermal (FLIR), anime, noir, snow → set_visual_style. Map, map without labels, satellite with labels (hybrid), satellite, photorealistic 3D (3d) → set_map_source.\n" +
            "When a request needs a tool, call the tool first and do not speak in the same response. If a request has several changes, call every tool before speaking.\n" +
            "After the tool output, speak one short confirmation of the RESULTING state (\"Line LM-04 selected\", \"Trip 278 is on Aisha Johnson's vehicle\"). On ok=false, say plainly what failed. Never claim an action without ok=true.\n" +
            "Never read out patient names, phone numbers or addresses, even if the dispatcher says them.";

        public static JsonArray Definitions() => new()
        {
            Tool("select_line", "Select one line (vehicle route of the day) on the live map, as a click on its vehicle does, and frame its route.",
                Props(("query", Str("Line name, driver name, vehicle name or plate, as the dispatcher said it."))), "query"),
            Tool("find_trip", "Find today's trip by its Trip Id and select the vehicle carrying it; the Trip Id is shown on the vehicle.",
                Props(("tripId", Str("The Trip Id, Raphael's number or the broker's code."))), "tripId"),
            Tool("fly_to_place", "Move the map to a city, address or place.",
                Props(("query", Str("Plain place search, e.g. \"Cape Coral\" or \"Lee Memorial Hospital\"."))), "query"),
            Tool("zoom_map", "Zoom the map in or out from where it is.",
                Props(("direction", Enum("in", "out")), ("steps", new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 4, ["description"] = "1 for a little, 2 normal, 4 a lot." })), "direction"),
            Tool("fit_fleet", "Frame every vehicle shown on the map.", Props()),
            Tool("clear_selection", "Let go of the selected line and close its card.", Props()),
            Tool("get_fleet_status", "Counts of lines by state and the list of lines with driver and state, to answer questions about the fleet.", Props()),
            Tool("set_visual_style", "Change the visual preset of the map.", Props(("style", Enum(Styles))), "style"),
            Tool("set_map_source", "Change the map type.", Props(("source", Enum(Sources))), "source"),
        };

        private static JsonObject Tool(string name, string description, JsonObject properties, params string[] required) => new()
        {
            ["type"] = "function",
            ["name"] = name,
            ["description"] = description,
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JsonArray(System.Array.ConvertAll(required, r => (JsonNode)r!)),
                ["additionalProperties"] = false
            }
        };

        private static JsonObject Props(params (string Name, JsonObject Schema)[] items)
        {
            var o = new JsonObject();
            foreach (var (n, s) in items) o[n] = s;
            return o;
        }

        private static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };

        private static JsonObject Enum(params string[] values) =>
            new() { ["type"] = "string", ["enum"] = new JsonArray(System.Array.ConvertAll(values, v => (JsonNode)v!)) };
    }
}
