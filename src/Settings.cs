using System.Text.Json;
using System.Text.Json.Serialization;

namespace router;

internal sealed class RouteGroup
{
    public bool Enabled { get; set; }
    public List<Rule> Rules { get; set; } = [];
}

internal sealed class Settings
{
    internal const int MaxRules = 12;
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public int Version { get; set; } = 4;
    public List<RouteGroup> Groups { get; set; } = [];

    internal static Settings Defaults() => new()
    {
        Groups =
        [
            new() { Enabled = true, Rules =
            [
                new(Room.Elite, Measure.Count, Preference.Most),
                new(Room.Elite, Measure.AverageFloor, Preference.Most),
                new(Room.RestSite, Measure.Count, Preference.Most),
                new(Room.Shop, Measure.Count, Preference.Most),
                new(Room.Unknown, Measure.Count, Preference.Most)
            ] },
            new() { Enabled = true, Rules =
            [
                new(Room.RestSite, Measure.Count, Preference.Most),
                new(Room.RestSite, Measure.AverageFloor, Preference.Fewest),
                new(Room.Shop, Measure.AverageFloor, Preference.Most),
                new(Room.Elite, Measure.AverageFloor, Preference.Most),
                new(Room.Unknown, Measure.Count, Preference.Most)
            ] },
            new(),
            new(),
            new()
        ]
    };

    internal static Settings Parse(string text)
    {
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("Version", out var property) || property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out int version) || version is < 1 or > 4)
            throw new JsonException("Unsupported settings.");
        var settings = version < 4 ? Upgrade(text, version) : JsonSerializer.Deserialize<Settings>(text, Json);
        if (settings?.Groups == null || settings.Groups.Count != 5) throw new JsonException("Unsupported settings.");
        foreach (var group in settings.Groups)
        {
            if (group == null || group.Rules == null || group.Rules.Count > MaxRules ||
                group.Rules.Any(rule => rule == null || !Enum.IsDefined(rule.Room) || !Enum.IsDefined(rule.Measure) || !Enum.IsDefined(rule.Preference) ||
                    rule.Measure == Measure.AverageFloor && rule.Preference is Preference.Present or Preference.Absent))
                throw new JsonException("Invalid route priorities.");
        }
        return settings;
    }

    private sealed record LegacyRule(Room Room, string Segment, Preference Preference);
    private sealed record LegacyGroup(bool Enabled, List<LegacyRule> Rules);
    private sealed record LegacySettings(List<LegacyGroup> Groups);

    private static Settings Upgrade(string text, int version)
    {
        var legacy = JsonSerializer.Deserialize<LegacySettings>(text, Json);
        if (legacy?.Groups == null || legacy.Groups.Count != 5) throw new JsonException("Unsupported settings.");
        foreach (var group in legacy.Groups)
            if (group?.Rules == null || group.Rules.Count > MaxRules || group.Rules.Any(rule => rule == null ||
                !Enum.IsDefined(rule.Room) || !Enum.IsDefined(rule.Preference) || rule.Segment is not ("WholeAct" or "BeforeChest" or "AfterChest")))
                throw new JsonException("Invalid route priorities.");
        if (version == 1) (legacy.Groups[2], legacy.Groups[4]) = (legacy.Groups[4], legacy.Groups[2]);
        var settings = new Settings();
        foreach (var group in legacy.Groups)
        {
            settings.Groups.Add(new RouteGroup
            {
                Enabled = group.Enabled,
                Rules = group.Rules.Select(rule => rule.Segment == "WholeAct"
                    ? new Rule(rule.Room, Measure.Count, rule.Preference)
                    : new Rule(rule.Room, Measure.AverageFloor,
                        (rule.Segment == "AfterChest") == (rule.Preference is Preference.Most or Preference.Present) ? Preference.Most : Preference.Fewest)).ToList()
            });
        }
        if (version < 3)
        {
            LegacyRule[][] previous =
            [
                [new(Room.Elite, "WholeAct", Preference.Most), new(Room.RestSite, "AfterChest", Preference.Most),
                    new(Room.Shop, "AfterChest", Preference.Present), new(Room.Unknown, "WholeAct", Preference.Most)],
                [new(Room.RestSite, "WholeAct", Preference.Most), new(Room.Elite, "BeforeChest", Preference.Absent),
                    new(Room.Shop, "AfterChest", Preference.Present), new(Room.Unknown, "WholeAct", Preference.Most)]
            ];
            var defaults = Defaults();
            for (int i = 0; i < previous.Length; i++)
                if (legacy.Groups[i].Rules.SequenceEqual(previous[i])) settings.Groups[i].Rules = defaults.Groups[i].Rules;
        }
        return settings;
    }

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, Json));
        File.Move(temporary, path, true);
    }
}
