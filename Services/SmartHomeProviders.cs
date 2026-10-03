using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IdleDash.Core;

namespace IdleDash.Services;

public enum DeviceKind { Light, Switch, Scene, Script, Button, Sensor, Binary, Event, Lock, Alarm, Cover, Climate, Group }

public enum SmartAction { Toggle, Brightness, Activate, Lock, Unlock, Arm, Disarm, Open, Close, Stop, TurnOn, TurnOff, SetColor, SetTemperature }

/// <summary>Eén apparaat, sensor of scène uit een smarthome-systeem.</summary>
public record SmartDevice(
    string Key,              // bv. "ha:light.woonkamer", "hue:light:3", "shelly:192.168.1.20:switch:0"
    string Name,
    DeviceKind Kind,
    bool? IsOn,
    int? Brightness,         // 0-100
    string? Value,           // tekst voor sensoren, bv. "21,5 °C"
    string Source,           // "Home Assistant", "Philips Hue", "Shelly"
    bool Sensitive,          // slot, alarm, garagedeur: altijd met bevestiging
    bool NeedsCode = false,  // alarm met pincode
    LightColor? Color = null); // wat een lamp met kleur en wittint kan, en hoe hij nu staat

/// <summary>Kleurmogelijkheden van een lamp en zijn huidige kleur (tint 0-360, verzadiging 0-100) of wittint (Kelvin).</summary>
public record LightColor(bool SupportsColor, bool SupportsTemperature, int MinKelvin, int MaxKelvin,
    double? Hue, double? Saturation, int? Kelvin);

public interface ISmartProvider
{
    string Name { get; }
    string Prefix { get; }
    Task<List<SmartDevice>> GetDevicesAsync(CancellationToken cancel);
    /// <summary>value: helderheid (%), tint (SetColor) of Kelvin (SetTemperature). value2: verzadiging bij SetColor.</summary>
    Task InvokeAsync(SmartDevice device, SmartAction action, int? value, int? value2, string? code, CancellationToken cancel);
}

internal static class SmartHttp
{
    public static readonly HttpClient Client = Create();

    private static HttpClient Create()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IdleDash/1.0");
        return client;
    }

    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static double? Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    public static bool? Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    public static JsonElement Obj(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    public static string Number(double value, int decimals = 1) =>
        Math.Round(value, decimals).ToString(decimals == 0 ? "0" : "0.#", Loc.Culture);
}

// ═══════════════════════════ Home Assistant ═══════════════════════════

/// <summary>Home Assistant via de REST-API met een "long-lived access token".</summary>
public sealed class HomeAssistantProvider : ISmartProvider
{
    private readonly Uri _base;
    private readonly string _token;

    public string Name => "Home Assistant";
    public string Prefix => "ha";

    public HomeAssistantProvider(string url, string token)
    {
        _base = new Uri(url.TrimEnd('/') + "/");
        _token = token;
    }

    public async Task<bool> TestAsync(CancellationToken cancel = default)
    {
        using var response = await Send(HttpMethod.Get, "api/", null, cancel);
        return response.IsSuccessStatusCode;
    }

    public async Task<List<SmartDevice>> GetDevicesAsync(CancellationToken cancel)
    {
        using var response = await Send(HttpMethod.Get, "api/states", null, cancel);
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException();
        response.EnsureSuccessStatusCode();
        return ParseStates(await response.Content.ReadAsStringAsync(cancel));
    }

    public async Task InvokeAsync(SmartDevice device, SmartAction action, int? value, int? value2, string? code, CancellationToken cancel)
    {
        var plan = Plan(device, action, value, code, value2) ?? throw new InvalidOperationException(Loc.T("Dit kan niet bij dit apparaat."));
        using var response = await Send(HttpMethod.Post, "api/services/" + plan.Service, JsonSerializer.Serialize(plan.Body), cancel);
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException();
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? json, CancellationToken cancel)
    {
        var request = new HttpRequestMessage(method, new Uri(_base, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return await SmartHttp.Client.SendAsync(request, cancel);
    }

    /// <summary>Welke dienst (domein/service) en welke gegevens bij een actie horen.</summary>
    public static (string Service, Dictionary<string, object> Body)? Plan(SmartDevice device, SmartAction action, int? value, string? code, int? value2 = null)
    {
        string entity = device.Key.StartsWith("ha:") ? device.Key[3..] : device.Key;
        string domain = entity.Split('.')[0];
        var body = new Dictionary<string, object> { ["entity_id"] = entity };
        if (!string.IsNullOrEmpty(code)) body["code"] = code;

        string? service = (domain, action) switch
        {
            ("light", SmartAction.Toggle) => "light/toggle",
            ("light" or "switch" or "input_boolean" or "fan" or "siren" or "humidifier", SmartAction.TurnOn) => domain + "/turn_on",
            ("light" or "switch" or "input_boolean" or "fan" or "siren" or "humidifier", SmartAction.TurnOff) => domain + "/turn_off",
            ("light", SmartAction.SetColor or SmartAction.SetTemperature) => "light/turn_on",
            ("light", SmartAction.Brightness) => "light/turn_on",
            ("switch" or "input_boolean" or "fan" or "siren" or "humidifier", SmartAction.Toggle) => domain + "/toggle",
            ("scene", SmartAction.Activate) => "scene/turn_on",
            ("script", SmartAction.Activate) => "script/turn_on",
            ("button", SmartAction.Activate) => "button/press",
            ("input_button", SmartAction.Activate) => "input_button/press",
            ("lock", SmartAction.Lock) => "lock/lock",
            ("lock", SmartAction.Unlock) => "lock/unlock",
            ("alarm_control_panel", SmartAction.Arm) => "alarm_control_panel/alarm_arm_away",
            ("alarm_control_panel", SmartAction.Disarm) => "alarm_control_panel/alarm_disarm",
            ("cover", SmartAction.Open) => "cover/open_cover",
            ("cover", SmartAction.Close) => "cover/close_cover",
            ("cover", SmartAction.Stop) => "cover/stop_cover",
            _ => null,
        };
        if (service == null) return null;
        if (action == SmartAction.Brightness) body["brightness_pct"] = Math.Clamp(value ?? 100, 1, 100);
        if (action == SmartAction.SetColor) body["hs_color"] = new[] { Math.Clamp(value ?? 0, 0, 360), Math.Clamp(value2 ?? 100, 0, 100) };
        if (action == SmartAction.SetTemperature) body["color_temp_kelvin"] = Math.Clamp(value ?? 4000, 1000, 12000);
        return (service, body);
    }

    /// <summary>Kleur en wittint van een lamp, uit supported_color_modes en de huidige stand.</summary>
    internal static LightColor? HaColor(JsonElement attributes)
    {
        var modes = new List<string>();
        if (attributes.ValueKind == JsonValueKind.Object && attributes.TryGetProperty("supported_color_modes", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var m in list.EnumerateArray())
                if (m.ValueKind == JsonValueKind.String) modes.Add(m.GetString()!);
        bool color = modes.Any(m => m is "hs" or "xy" or "rgb" or "rgbw" or "rgbww");
        bool temperature = modes.Contains("color_temp");
        if (!color && !temperature) return null;

        double? hue = null, saturation = null;
        int? kelvin = null;
        string mode = SmartHttp.Str(attributes, "color_mode") ?? "";
        if (mode == "color_temp")
        {
            kelvin = SmartHttp.Num(attributes, "color_temp_kelvin") is double k ? (int)Math.Round(k) : null;
        }
        else if (attributes.ValueKind == JsonValueKind.Object && attributes.TryGetProperty("hs_color", out var hs)
                 && hs.ValueKind == JsonValueKind.Array && hs.GetArrayLength() == 2
                 && hs[0].ValueKind == JsonValueKind.Number && hs[1].ValueKind == JsonValueKind.Number)
        {
            hue = hs[0].GetDouble();
            saturation = hs[1].GetDouble();
        }
        int min = SmartHttp.Num(attributes, "min_color_temp_kelvin") is double lo ? (int)lo : 2000;
        int max = SmartHttp.Num(attributes, "max_color_temp_kelvin") is double hi ? (int)hi : 6500;
        return new LightColor(color, temperature, min, max, hue, saturation, kelvin);
    }

    public static List<SmartDevice> ParseStates(string json)
    {
        var devices = new List<SmartDevice>();
        using var doc = JsonDocument.Parse(json);
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            string? id = SmartHttp.Str(e, "entity_id");
            string state = SmartHttp.Str(e, "state") ?? "";
            if (id == null) continue;
            var attributes = SmartHttp.Obj(e, "attributes");
            string name = SmartHttp.Str(attributes, "friendly_name") ?? id;
            string domain = id.Split('.')[0];
            string deviceClass = SmartHttp.Str(attributes, "device_class") ?? "";
            bool unavailable = state is "unavailable" or "unknown" or "";
            bool? on = unavailable ? null : state == "on";

            SmartDevice? device = domain switch
            {
                "light" => new(id, name, DeviceKind.Light, on,
                    SmartHttp.Num(attributes, "brightness") is double b ? (int)Math.Round(b / 255 * 100) : on == true ? 100 : 0, null, "Home Assistant", false, Color: HaColor(attributes)),
                "switch" or "input_boolean" or "fan" or "siren" or "humidifier" => new(id, name, DeviceKind.Switch, on, null, null, "Home Assistant", false),
                "scene" => new(id, name, DeviceKind.Scene, null, null, null, "Home Assistant", false),
                "script" => new(id, name, DeviceKind.Script, state == "on", null, null, "Home Assistant", false),
                "button" or "input_button" => new(id, name, DeviceKind.Button, null, null, null, "Home Assistant", false),
                "sensor" => new(id, name, DeviceKind.Sensor, null, null,
                    unavailable ? "–" : FormatSensor(state, SmartHttp.Str(attributes, "unit_of_measurement")), "Home Assistant", false),
                "binary_sensor" => new(id, name, DeviceKind.Binary, on, null, unavailable ? "–" : BinaryText(deviceClass, state == "on"), "Home Assistant", false),
                "event" => new(id, name, DeviceKind.Event, null, null, state, "Home Assistant", false),
                "lock" => new(id, name, DeviceKind.Lock, unavailable ? null : state == "locked", null,
                    unavailable ? "–" : state == "locked" ? Loc.T("Op slot") : state == "unlocked" ? Loc.T("Open") : state, "Home Assistant", true,
                    NeedsCode: SmartHttp.Str(attributes, "code_format") != null),
                "alarm_control_panel" => new(id, name, DeviceKind.Alarm, unavailable ? null : state.StartsWith("armed"), null,
                    AlarmText(state), "Home Assistant", true, NeedsCode: SmartHttp.Str(attributes, "code_format") != null),
                "cover" => new(id, name, DeviceKind.Cover, unavailable ? null : state is "open" or "opening", null,
                    CoverText(state), "Home Assistant", deviceClass is "garage" or "gate" or "door"),
                "climate" => new(id, name, DeviceKind.Climate, state != "off", null,
                    SmartHttp.Num(attributes, "current_temperature") is double t ? Loc.Degrees(t) : state, "Home Assistant", false),
                _ => null,
            };
            if (device != null) devices.Add(device with { Key = "ha:" + id });
        }
        return devices.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static string FormatSensor(string state, string? unit)
    {
        if (double.TryParse(state, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            state = SmartHttp.Number(v);
        return string.IsNullOrEmpty(unit) ? state : state + " " + unit;
    }

    private static string BinaryText(string deviceClass, bool on) => deviceClass switch
    {
        "door" or "window" or "opening" or "garage_door" => on ? Loc.T("Open") : Loc.T("Dicht"),
        "motion" or "occupancy" or "presence" => on ? Loc.T("Beweging") : Loc.T("Geen beweging"),
        "moisture" => on ? Loc.T("Nat") : Loc.T("Droog"),
        "smoke" or "gas" or "carbon_monoxide" or "problem" or "safety" => on ? Loc.T("Alarm!") : Loc.T("Veilig"),
        _ => on ? Loc.T("Aan") : Loc.T("Uit"),
    };

    private static string AlarmText(string state) => state switch
    {
        "disarmed" => Loc.T("Uitgeschakeld"),
        "armed_away" or "armed_home" or "armed_night" or "armed_vacation" or "armed_custom_bypass" => Loc.T("Ingeschakeld"),
        "arming" or "pending" => Loc.T("Bezig…"),
        "triggered" => Loc.T("Alarm!"),
        _ => "–",
    };

    private static string CoverText(string state) => state switch
    {
        "open" => Loc.T("Open"),
        "closed" => Loc.T("Dicht"),
        "opening" => Loc.T("Gaat open…"),
        "closing" => Loc.T("Gaat dicht…"),
        _ => "–",
    };
}

// ═══════════════════════════ Philips Hue ═══════════════════════════

/// <summary>Philips Hue via de bridge in je eigen netwerk.</summary>
public sealed class HueProvider : ISmartProvider
{
    private readonly string _bridge;
    private readonly string _user;

    public string Name => "Philips Hue";
    public string Prefix => "hue";

    public HueProvider(string bridge, string user)
    {
        _bridge = bridge.Trim();
        _user = user.Trim();
    }

    private string Url(string path) => $"http://{_bridge}/api/{_user}/{path}";

    public async Task<List<SmartDevice>> GetDevicesAsync(CancellationToken cancel)
    {
        string lights = await Get("lights", cancel);
        string groups = await Get("groups", cancel);
        string scenes = await Get("scenes", cancel);
        string sensors = await Get("sensors", cancel);
        return ParseLights(lights).Concat(ParseGroups(groups)).Concat(ParseScenes(scenes)).Concat(ParseSensors(sensors))
            .OrderBy(d => d.Kind == DeviceKind.Scene).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private async Task<string> Get(string path, CancellationToken cancel)
    {
        string json = await SmartHttp.Client.GetStringAsync(Url(path), cancel);
        ThrowIfError(json);
        return json;
    }

    public async Task InvokeAsync(SmartDevice device, SmartAction action, int? value, int? value2, string? code, CancellationToken cancel)
    {
        var parts = device.Key.Split(':');   // hue:light:3, hue:group:1, hue:scene:abc:1
        string? path = null, body = null;
        switch (parts[1], action)
        {
            case ("light", SmartAction.Toggle):
                path = $"lights/{parts[2]}/state"; body = $"{{\"on\":{(device.IsOn == true ? "false" : "true")}}}"; break;
            case ("light", SmartAction.Brightness):
                path = $"lights/{parts[2]}/state"; body = $"{{\"on\":true,\"bri\":{Bri(value)}}}"; break;
            case ("group", SmartAction.Toggle):
                path = $"groups/{parts[2]}/action"; body = $"{{\"on\":{(device.IsOn == true ? "false" : "true")}}}"; break;
            case ("group", SmartAction.Brightness):
                path = $"groups/{parts[2]}/action"; body = $"{{\"on\":true,\"bri\":{Bri(value)}}}"; break;
            case ("light" or "group", SmartAction.TurnOn or SmartAction.TurnOff):
                path = Target(parts); body = action == SmartAction.TurnOn ? "{\"on\":true}" : "{\"on\":false}"; break;
            case ("light" or "group", SmartAction.SetColor):
                path = Target(parts);
                body = $"{{\"on\":true,\"hue\":{ColorMath.ToHueApiHue(value ?? 0)},\"sat\":{ColorMath.ToHueApiSat(value2 ?? 100)}}}"; break;
            case ("light" or "group", SmartAction.SetTemperature):
                path = Target(parts); body = $"{{\"on\":true,\"ct\":{Math.Clamp(ColorMath.KelvinToMired(value ?? 4000), 153, 500)}}}"; break;
            case ("scene", SmartAction.Activate) when parts.Length >= 4:
                path = $"groups/{parts[3]}/action"; body = $"{{\"scene\":\"{parts[2]}\"}}"; break;
        }
        if (path == null || body == null) throw new InvalidOperationException(Loc.T("Dit kan niet bij dit apparaat."));
        using var response = await SmartHttp.Client.PutAsync(Url(path), new StringContent(body, Encoding.UTF8, "application/json"), cancel);
        response.EnsureSuccessStatusCode();
        ThrowIfError(await response.Content.ReadAsStringAsync(cancel));
    }

    private static int Bri(int? percent) => Math.Clamp((int)Math.Round((percent ?? 100) * 2.54), 1, 254);

    private static void ThrowIfError(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var error = SmartHttp.Obj(item, "error");
            if (error.ValueKind != JsonValueKind.Object) continue;
            if (SmartHttp.Num(error, "type") == 1) throw new UnauthorizedAccessException();
            throw new InvalidOperationException(SmartHttp.Str(error, "description") ?? "Hue");
        }
    }

    private static string Target(string[] parts) => parts[1] == "light" ? $"lights/{parts[2]}/state" : $"groups/{parts[2]}/action";

    /// <summary>Kleur en wittint van een Hue-lamp (of kamer: dan zonder type, op basis van wat de kamer meldt).</summary>
    internal static LightColor? HueColor(string? type, JsonElement state, JsonElement ctRange)
    {
        bool color = type is "Extended color light" or "Color light" || (type == null && SmartHttp.Num(state, "hue") != null);
        bool temperature = type is "Extended color light" or "Color temperature light" || (type == null && SmartHttp.Num(state, "ct") != null);
        if (!color && !temperature) return null;

        double? hue = null, saturation = null;
        int? kelvin = null;
        if (SmartHttp.Str(state, "colormode") == "ct" && SmartHttp.Num(state, "ct") is double ct)
            kelvin = ColorMath.MiredToKelvin((int)ct);
        else if (color && SmartHttp.Num(state, "hue") is double h && SmartHttp.Num(state, "sat") is double s)
            (hue, saturation) = (ColorMath.FromHueApiHue(h), ColorMath.FromHueApiSat(s));
        int minMired = SmartHttp.Num(ctRange, "min") is double lo ? (int)lo : 153;
        int maxMired = SmartHttp.Num(ctRange, "max") is double hi ? (int)hi : 500;
        return new LightColor(color, temperature, ColorMath.MiredToKelvin(maxMired), ColorMath.MiredToKelvin(minMired), hue, saturation, kelvin);
    }

    public static List<SmartDevice> ParseLights(string json)
    {
        var list = new List<SmartDevice>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return list;
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            var state = SmartHttp.Obj(p.Value, "state");
            bool reachable = SmartHttp.Bool(state, "reachable") ?? true;
            bool? on = reachable ? SmartHttp.Bool(state, "on") : null;
            int? bri = SmartHttp.Num(state, "bri") is double b ? (int)Math.Round(b / 2.54) : null;
            var ctRange = SmartHttp.Obj(SmartHttp.Obj(SmartHttp.Obj(p.Value, "capabilities"), "control"), "ct");
            list.Add(new SmartDevice($"hue:light:{p.Name}", SmartHttp.Str(p.Value, "name") ?? p.Name, DeviceKind.Light, on, bri, null, "Philips Hue", false,
                Color: HueColor(SmartHttp.Str(p.Value, "type"), state, ctRange)));
        }
        return list;
    }

    public static List<SmartDevice> ParseGroups(string json)
    {
        var list = new List<SmartDevice>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return list;
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            string type = SmartHttp.Str(p.Value, "type") ?? "";
            if (type is not ("Room" or "Zone")) continue;
            bool? on = SmartHttp.Bool(SmartHttp.Obj(p.Value, "state"), "any_on");
            int? bri = SmartHttp.Num(SmartHttp.Obj(p.Value, "action"), "bri") is double b ? (int)Math.Round(b / 2.54) : null;
            list.Add(new SmartDevice($"hue:group:{p.Name}", SmartHttp.Str(p.Value, "name") ?? p.Name, DeviceKind.Light, on, bri, null, "Philips Hue", false,
                Color: HueColor(null, SmartHttp.Obj(p.Value, "action"), default)));
        }
        return list;
    }

    public static List<SmartDevice> ParseScenes(string json)
    {
        var list = new List<SmartDevice>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return list;
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            string? group = SmartHttp.Str(p.Value, "group");
            if (group == null) continue;   // oude scènes zonder kamer kunnen niet los aangezet worden
            list.Add(new SmartDevice($"hue:scene:{p.Name}:{group}", SmartHttp.Str(p.Value, "name") ?? p.Name, DeviceKind.Scene, null, null, null, "Philips Hue", false));
        }
        return list;
    }

    public static List<SmartDevice> ParseSensors(string json)
    {
        var list = new List<SmartDevice>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return list;
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            string type = SmartHttp.Str(p.Value, "type") ?? "";
            string name = SmartHttp.Str(p.Value, "name") ?? p.Name;
            var state = SmartHttp.Obj(p.Value, "state");
            if (type == "ZLLTemperature" && SmartHttp.Num(state, "temperature") is double t)
                list.Add(new SmartDevice($"hue:sensor:{p.Name}", name, DeviceKind.Sensor, null, null, Loc.Degrees(t / 100), "Philips Hue", false));
            else if (type == "ZLLPresence" && SmartHttp.Bool(state, "presence") is bool present)
                list.Add(new SmartDevice($"hue:sensor:{p.Name}", name, DeviceKind.Binary, present, null,
                    present ? Loc.T("Beweging") : Loc.T("Geen beweging"), "Philips Hue", false));
        }
        return list;
    }

    /// <summary>Bridges in je netwerk zoeken (via de dienst van Philips).</summary>
    public static async Task<List<string>> DiscoverAsync()
    {
        try
        {
            using var doc = JsonDocument.Parse(await SmartHttp.Client.GetStringAsync("https://discovery.meethue.com/"));
            return doc.RootElement.EnumerateArray().Select(e => SmartHttp.Str(e, "internalipaddress")).OfType<string>().ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>Koppelen: werkt alleen binnen 30 seconden nadat je op de knop van de bridge hebt gedrukt.</summary>
    public static async Task<(string? User, bool ButtonNotPressed)> PairAsync(string bridge)
    {
        using var response = await SmartHttp.Client.PostAsync($"http://{bridge.Trim()}/api",
            new StringContent("{\"devicetype\":\"idledash#windows\"}", Encoding.UTF8, "application/json"));
        return ParsePair(await response.Content.ReadAsStringAsync());
    }

    public static (string? User, bool ButtonNotPressed) ParsePair(string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (SmartHttp.Str(SmartHttp.Obj(item, "success"), "username") is string user) return (user, false);
            if (SmartHttp.Num(SmartHttp.Obj(item, "error"), "type") == 101) return (null, true);
        }
        return (null, false);
    }
}

// ═══════════════════════════ Shelly ═══════════════════════════

/// <summary>Shelly-schakelaars, -dimmers en -rolluiken (generatie 1 en 2+), per IP-adres.</summary>
public sealed class ShellyProvider : ISmartProvider
{
    private readonly List<string> _hosts;
    private readonly Dictionary<string, int> _generation = new();

    public string Name => "Shelly";
    public string Prefix => "shelly";

    public ShellyProvider(IEnumerable<string> hosts) => _hosts = hosts.Select(h => h.Trim()).Where(h => h.Length > 0).ToList();

    public async Task<List<SmartDevice>> GetDevicesAsync(CancellationToken cancel)
    {
        var all = new List<SmartDevice>();
        foreach (string host in _hosts)
        {
            try
            {
                int gen = await GenerationAsync(host, cancel);
                if (gen >= 2)
                {
                    string status = await SmartHttp.Client.GetStringAsync($"http://{host}/rpc/Shelly.GetStatus", cancel);
                    string config = await SmartHttp.Client.GetStringAsync($"http://{host}/rpc/Shelly.GetConfig", cancel);
                    all.AddRange(ParseGen2(host, status, config));
                }
                else
                {
                    string status = await SmartHttp.Client.GetStringAsync($"http://{host}/status", cancel);
                    string settings = await SmartHttp.Client.GetStringAsync($"http://{host}/settings", cancel);
                    all.AddRange(ParseGen1(host, status, settings));
                }
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Dit apparaat is even niet bereikbaar: de andere gewoon tonen
            }
        }
        return all;
    }

    private async Task<int> GenerationAsync(string host, CancellationToken cancel)
    {
        if (_generation.TryGetValue(host, out int gen)) return gen;
        using var doc = JsonDocument.Parse(await SmartHttp.Client.GetStringAsync($"http://{host}/shelly", cancel));
        gen = SmartHttp.Num(doc.RootElement, "gen") is double g ? (int)g : 1;
        _generation[host] = gen;
        return gen;
    }

    public async Task InvokeAsync(SmartDevice device, SmartAction action, int? value, int? value2, string? code, CancellationToken cancel)
    {
        string host = device.Key.Split(':')[1];
        int gen = await GenerationAsync(host, cancel);
        string url = PlanUrl(device, action, value, gen) ?? throw new InvalidOperationException(Loc.T("Dit kan niet bij dit apparaat."));
        using var response = await SmartHttp.Client.GetAsync(url, cancel);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Het adres dat de actie uitvoert. Sleutel: shelly:host:soort:nummer.</summary>
    public static string? PlanUrl(SmartDevice device, SmartAction action, int? value, int generation)
    {
        var p = device.Key.Split(':');
        if (p.Length < 4) return null;
        string host = p[1], kind = p[2], id = p[3];
        int brightness = Math.Clamp(value ?? 100, 1, 100);
        if (generation >= 2)
        {
            return (kind, action) switch
            {
                ("switch", SmartAction.Toggle) => $"http://{host}/rpc/Switch.Toggle?id={id}",
                ("switch", SmartAction.TurnOn) => $"http://{host}/rpc/Switch.Set?id={id}&on=true",
                ("switch", SmartAction.TurnOff) => $"http://{host}/rpc/Switch.Set?id={id}&on=false",
                ("light", SmartAction.TurnOn) => $"http://{host}/rpc/Light.Set?id={id}&on=true",
                ("light", SmartAction.TurnOff) => $"http://{host}/rpc/Light.Set?id={id}&on=false",
                ("light", SmartAction.Toggle) => $"http://{host}/rpc/Light.Toggle?id={id}",
                ("light", SmartAction.Brightness) => $"http://{host}/rpc/Light.Set?id={id}&on=true&brightness={brightness}",
                ("cover", SmartAction.Open) => $"http://{host}/rpc/Cover.Open?id={id}",
                ("cover", SmartAction.Close) => $"http://{host}/rpc/Cover.Close?id={id}",
                ("cover", SmartAction.Stop) => $"http://{host}/rpc/Cover.Stop?id={id}",
                _ => null,
            };
        }
        return (kind, action) switch
        {
            ("switch", SmartAction.Toggle) => $"http://{host}/relay/{id}?turn=toggle",
            ("switch", SmartAction.TurnOn) => $"http://{host}/relay/{id}?turn=on",
            ("switch", SmartAction.TurnOff) => $"http://{host}/relay/{id}?turn=off",
            ("light", SmartAction.TurnOn) => $"http://{host}/light/{id}?turn=on",
            ("light", SmartAction.TurnOff) => $"http://{host}/light/{id}?turn=off",
            ("light", SmartAction.Toggle) => $"http://{host}/light/{id}?turn=toggle",
            ("light", SmartAction.Brightness) => $"http://{host}/light/{id}?turn=on&brightness={brightness}",
            ("cover", SmartAction.Open) => $"http://{host}/roller/{id}?go=open",
            ("cover", SmartAction.Close) => $"http://{host}/roller/{id}?go=close",
            ("cover", SmartAction.Stop) => $"http://{host}/roller/{id}?go=stop",
            _ => null,
        };
    }

    public static List<SmartDevice> ParseGen2(string host, string statusJson, string configJson)
    {
        var list = new List<SmartDevice>();
        using var status = JsonDocument.Parse(statusJson);
        using var config = JsonDocument.Parse(configJson);
        string deviceName = SmartHttp.Str(SmartHttp.Obj(SmartHttp.Obj(config.RootElement, "sys"), "device"), "name") ?? "Shelly " + host;

        foreach (var p in status.RootElement.EnumerateObject())
        {
            var key = p.Name.Split(':');
            if (key.Length != 2) continue;
            string own = SmartHttp.Str(SmartHttp.Obj(config.RootElement, p.Name), "name") ?? "";
            string name = own.Length > 0 ? own : deviceName + (key[1] == "0" ? "" : " " + (int.Parse(key[1], CultureInfo.InvariantCulture) + 1));
            switch (key[0])
            {
                case "switch":
                    string? power = SmartHttp.Num(p.Value, "apower") is double w ? SmartHttp.Number(w, 0) + " W" : null;
                    list.Add(new SmartDevice($"shelly:{host}:switch:{key[1]}", name, DeviceKind.Switch, SmartHttp.Bool(p.Value, "output"), null, power, "Shelly", false));
                    break;
                case "light":
                    list.Add(new SmartDevice($"shelly:{host}:light:{key[1]}", name, DeviceKind.Light, SmartHttp.Bool(p.Value, "output"),
                        SmartHttp.Num(p.Value, "brightness") is double b ? (int)b : null, null, "Shelly", false));
                    break;
                case "cover":
                    string state = SmartHttp.Str(p.Value, "state") ?? "";
                    list.Add(new SmartDevice($"shelly:{host}:cover:{key[1]}", name, DeviceKind.Cover, state is "open" or "opening", null,
                        state switch { "open" => Loc.T("Open"), "closed" => Loc.T("Dicht"), "opening" => Loc.T("Gaat open…"), "closing" => Loc.T("Gaat dicht…"), _ => "–" },
                        "Shelly", false));
                    break;
            }
        }
        return list;
    }

    public static List<SmartDevice> ParseGen1(string host, string statusJson, string settingsJson)
    {
        var list = new List<SmartDevice>();
        using var status = JsonDocument.Parse(statusJson);
        using var settings = JsonDocument.Parse(settingsJson);
        string deviceName = SmartHttp.Str(settings.RootElement, "name") is { Length: > 0 } n ? n : "Shelly " + host;

        void Add(string array, string kind, DeviceKind deviceKind)
        {
            if (!status.RootElement.TryGetProperty(array, out var items) || items.ValueKind != JsonValueKind.Array) return;
            var names = settings.RootElement.TryGetProperty(array, out var s) && s.ValueKind == JsonValueKind.Array ? s.EnumerateArray().ToList() : new();
            int i = 0;
            foreach (var item in items.EnumerateArray())
            {
                string own = i < names.Count ? SmartHttp.Str(names[i], "name") ?? "" : "";
                string name = own.Length > 0 ? own : deviceName + (i == 0 ? "" : " " + (i + 1));
                if (deviceKind == DeviceKind.Cover)
                {
                    string state = SmartHttp.Str(item, "state") ?? "";
                    list.Add(new SmartDevice($"shelly:{host}:{kind}:{i}", name, deviceKind, state == "open", null,
                        state switch { "open" => Loc.T("Open"), "close" => Loc.T("Dicht"), _ => "–" }, "Shelly", false));
                }
                else
                {
                    list.Add(new SmartDevice($"shelly:{host}:{kind}:{i}", name, deviceKind, SmartHttp.Bool(item, "ison"),
                        SmartHttp.Num(item, "brightness") is double b ? (int)b : null, null, "Shelly", false));
                }
                i++;
            }
        }

        Add("relays", "switch", DeviceKind.Switch);
        Add("lights", "light", DeviceKind.Light);
        Add("rollers", "cover", DeviceKind.Cover);
        return list;
    }
}


// ═══════════════════════════ Eigen groepen ═══════════════════════════

/// <summary>Groepen die je in IdleDash zelf maakt: lampen en schakelaars van verschillende merken als één tegel.</summary>
public static class SmartGroups
{
    public const string KeyPrefix = "group:";

    /// <summary>Wat er in een groep mag: lampen en schakelaars (geen sloten, alarm of deuren).</summary>
    public static bool CanJoin(SmartDevice device) =>
        device.Kind is DeviceKind.Light or DeviceKind.Switch && !device.Sensitive && !device.Key.StartsWith(KeyPrefix);

    public static List<SmartDevice> Build(IEnumerable<SmartGroupSettings> groups, IReadOnlyList<SmartDevice> devices)
    {
        var list = new List<SmartDevice>();
        foreach (var group in groups)
        {
            var members = devices.Where(d => group.Devices.Contains(d.Key) && CanJoin(d)).ToList();
            var known = members.Where(m => m.IsOn != null).ToList();
            bool? on = known.Count == 0 ? null : known.Any(m => m.IsOn == true);
            var lit = members.Where(m => m.IsOn == true && m.Kind == DeviceKind.Light && m.Brightness is > 0).ToList();
            int? brightness = members.Any(m => m.Kind == DeviceKind.Light)
                ? lit.Count > 0 ? (int)Math.Round(lit.Average(m => m.Brightness!.Value)) : 0
                : null;

            var colors = members.Select(m => m.Color).OfType<LightColor>().ToList();
            LightColor? color = null;
            if (colors.Count > 0)
            {
                var current = members.Where(m => m.IsOn == true).Select(m => m.Color).OfType<LightColor>().FirstOrDefault() ?? colors[0];
                var temps = colors.Where(c => c.SupportsTemperature).ToList();
                color = new LightColor(colors.Any(c => c.SupportsColor), temps.Count > 0,
                    temps.Count > 0 ? temps.Min(c => c.MinKelvin) : 2000, temps.Count > 0 ? temps.Max(c => c.MaxKelvin) : 6500,
                    current.Hue, current.Saturation, current.Kelvin);
            }
            string value = Loc.T("{0} van {1} aan", members.Count(m => m.IsOn == true), members.Count);
            list.Add(new SmartDevice(KeyPrefix + group.Id, group.Name, DeviceKind.Group, on, brightness, value, Loc.T("Groep"), false, Color: color));
        }
        return list;
    }

    /// <summary>
    /// Welke opdracht elk lid krijgt. De aan/uit-knop: brandt er iets in de groep, dan gaat alles uit, anders alles aan.
    /// Kleur en wittint gaan alleen naar lampen die dat kunnen.
    /// </summary>
    public static List<(SmartDevice Member, SmartAction Action)> Plan(SmartDevice group, IEnumerable<SmartDevice> members, SmartAction action)
    {
        var list = new List<(SmartDevice, SmartAction)>();
        foreach (var member in members)
        {
            SmartAction? step = action switch
            {
                SmartAction.Toggle => group.IsOn == true ? SmartAction.TurnOff : SmartAction.TurnOn,
                SmartAction.TurnOn or SmartAction.TurnOff => action,
                SmartAction.Brightness when member.Kind == DeviceKind.Light => action,
                SmartAction.SetColor when member.Color?.SupportsColor == true => action,
                SmartAction.SetTemperature when member.Color?.SupportsTemperature == true => action,
                _ => null,
            };
            if (step != null) list.Add((member, step.Value));
        }
        return list;
    }
}
