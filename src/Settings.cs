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

    public int Version { get; set; } = 1;
    public List<RouteGroup> Groups { get; set; } = [];

    internal static Settings Defaults() => new()
    {
        Groups =
        [
            new() { Enabled = true, Rules =
            [
                new(Room.Elite, Segment.WholeAct, Preference.Most),
                new(Room.RestSite, Segment.AfterChest, Preference.Most),
                new(Room.Shop, Segment.AfterChest, Preference.Present),
                new(Room.Unknown, Segment.WholeAct, Preference.Most)
            ] },
            new() { Enabled = true, Rules =
            [
                new(Room.RestSite, Segment.WholeAct, Preference.Most),
                new(Room.Elite, Segment.BeforeChest, Preference.Absent),
                new(Room.Shop, Segment.AfterChest, Preference.Present),
                new(Room.Unknown, Segment.WholeAct, Preference.Most)
            ] },
            new(),
            new(),
            new()
        ]
    };

    internal static Settings Parse(string text)
    {
        var settings = JsonSerializer.Deserialize<Settings>(text, Json) ?? throw new JsonException("Empty settings.");
        if (settings.Version != 1 || settings.Groups == null || settings.Groups.Count != 5)
            throw new JsonException("Unsupported settings.");
        foreach (var group in settings.Groups)
        {
            if (group == null || group.Rules == null || group.Rules.Count > MaxRules ||
                group.Rules.Any(rule => rule == null || !Enum.IsDefined(rule.Room) || !Enum.IsDefined(rule.Segment) || !Enum.IsDefined(rule.Preference)))
                throw new JsonException("Invalid route priorities.");
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
