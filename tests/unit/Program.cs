using System.Diagnostics;
using System.Text.Json;
using router;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++;
}

void Reject(Action action, string name)
{
    try { action(); }
    catch (Exception error) when (error is ArgumentException or JsonException or OperationCanceledException)
    {
        checks++;
        return;
    }
    throw new InvalidOperationException(name);
}

RoutePlan Oracle(Dictionary<int, MapNode> map, int[] starts, int[] goals, Rule[] rules)
{
    decimal?[]? best = null;
    var winners = new List<int[]>();
    void Walk(int id, List<int> path)
    {
        path.Add(id);
        if (goals.Contains(id))
        {
            var score = rules.Select(rule =>
            {
                var rooms = path.Select(step => map[step]).Where(node => node.Room == rule.Room).ToArray();
                if (rule.Measure == Measure.AverageFloor)
                    return rooms.Length == 0 ? (decimal?)null : rooms.Average(node => (decimal)node.Floor) * (rule.Preference == Preference.Fewest ? -1 : 1);
                return rule.Preference switch
                {
                    Preference.Most => rooms.Length,
                    Preference.Fewest => -rooms.Length,
                    Preference.Present => rooms.Length > 0 ? 1 : 0,
                    _ => rooms.Length > 0 ? -1 : 0
                };
            }).ToArray();
            int compare = best == null ? 1 : score.Zip(best, Nullable.Compare).FirstOrDefault(value => value != 0);
            if (compare > 0) { best = score; winners.Clear(); }
            if (compare >= 0) winners.Add(path.ToArray());
        }
        else foreach (int child in map[id].Children) Walk(child, path);
        path.RemoveAt(path.Count - 1);
    }
    foreach (int start in starts) Walk(start, []);
    return new RoutePlan(winners.SelectMany(p => p).ToHashSet(),
        winners.SelectMany(p => p.Zip(p.Skip(1), (from, to) => new Edge(from, to))).ToHashSet());
}

string Legacy(int version, (Room Room, string Segment, Preference Preference)[][] groups, int enabled = 3) =>
    JsonSerializer.Serialize(new
    {
        Version = version,
        Groups = Enumerable.Range(0, 5).Select(i => new
        {
            Name = "Old label",
            Enabled = (enabled & (1 << i)) != 0,
            Rules = (i < groups.Length ? groups[i] : []).Select(rule => new { rule.Room, rule.Segment, rule.Preference })
        })
    }, Settings.Json);

var defaults = Settings.Defaults();
Check(defaults.Groups.Count == 5 && defaults.Groups.Count(g => g.Enabled) == 2, "five groups with two enabled presets");
Check(defaults.Groups[0].Rules.SequenceEqual(new Rule[]
{
    new(Room.Elite, Measure.Count, Preference.Most), new(Room.Elite, Measure.AverageFloor, Preference.Most),
    new(Room.RestSite, Measure.Count, Preference.Most), new(Room.Shop, Measure.Count, Preference.Most), new(Room.Unknown, Measure.Count, Preference.Most)
}), "aggressive priorities use later elites after elite count");
Check(defaults.Groups[1].Rules.SequenceEqual(new Rule[]
{
    new(Room.RestSite, Measure.Count, Preference.Most), new(Room.RestSite, Measure.AverageFloor, Preference.Fewest),
    new(Room.Shop, Measure.AverageFloor, Preference.Most), new(Room.Elite, Measure.AverageFloor, Preference.Most), new(Room.Unknown, Measure.Count, Preference.Most)
}), "steady priorities use earlier rest sites and later shops and elites");
var roundtrip = Settings.Parse(JsonSerializer.Serialize(defaults, Settings.Json));
Check(roundtrip.Groups.SelectMany(group => group.Rules).SequenceEqual(defaults.Groups.SelectMany(group => group.Rules)), "priority settings roundtrip");
(Room, string, Preference)[][] previous =
[
    [(Room.Elite, "WholeAct", Preference.Most), (Room.Elite, "AfterChest", Preference.Most),
        (Room.RestSite, "WholeAct", Preference.Most), (Room.Shop, "WholeAct", Preference.Most), (Room.Unknown, "WholeAct", Preference.Most)],
    [(Room.RestSite, "WholeAct", Preference.Most), (Room.RestSite, "BeforeChest", Preference.Most),
        (Room.Shop, "AfterChest", Preference.Most), (Room.Elite, "AfterChest", Preference.Most), (Room.Unknown, "WholeAct", Preference.Most)]
];
var upgraded = Settings.Parse(Legacy(3, previous, 2));
Check(upgraded.Version == 4 && !upgraded.Groups[0].Enabled && upgraded.Groups[1].Enabled &&
    upgraded.Groups.Take(2).Select((group, i) => group.Rules.SequenceEqual(defaults.Groups[i].Rules)).All(match => match),
    "previous defaults migrate to averages and preserve visibility");
string migrated = JsonSerializer.Serialize(upgraded, Settings.Json);
Check(!migrated.Contains("Segment", StringComparison.Ordinal) && !migrated.Contains("Name", StringComparison.Ordinal), "saved settings drop chest scopes and group labels");
Check(Settings.Parse(migrated).Groups[1].Rules.SequenceEqual(defaults.Groups[1].Rules), "migration is stable on reload");
(Room, string, Preference)[][] early =
[
    [(Room.Elite, "WholeAct", Preference.Most), (Room.RestSite, "AfterChest", Preference.Most),
        (Room.Shop, "AfterChest", Preference.Present), (Room.Unknown, "WholeAct", Preference.Most)],
    [(Room.RestSite, "WholeAct", Preference.Most), (Room.Elite, "BeforeChest", Preference.Absent),
        (Room.Shop, "AfterChest", Preference.Present), (Room.Unknown, "WholeAct", Preference.Most)],
    [(Room.Shop, "WholeAct", Preference.Most)]
];
var oldPalette = Settings.Parse(Legacy(1, early, 7));
Check(oldPalette.Groups[4].Enabled && oldPalette.Groups[4].Rules.SequenceEqual([new Rule(Room.Shop, Measure.Count, Preference.Most)]) && !oldPalette.Groups[2].Enabled,
    "palette migration preserves custom priorities and visibility");
Check(oldPalette.Groups.Take(2).Select((group, i) => group.Rules.SequenceEqual(defaults.Groups[i].Rules)).All(match => match), "early unmodified presets migrate to current defaults");
early[1] = early[1].Reverse().ToArray();
var custom = Settings.Parse(Legacy(2, early));
Check(custom.Groups[1].Rules.SequenceEqual(new Rule[]
{
    new(Room.Unknown, Measure.Count, Preference.Most), new(Room.Shop, Measure.AverageFloor, Preference.Most),
    new(Room.Elite, Measure.AverageFloor, Preference.Most), new(Room.RestSite, Measure.Count, Preference.Most)
}), "custom order survives migration with equivalent direction");
var directions = Settings.Parse(Legacy(3,
[
    [(Room.Shop, "BeforeChest", Preference.Fewest), (Room.Shop, "AfterChest", Preference.Absent),
        (Room.Shop, "BeforeChest", Preference.Present), (Room.Shop, "AfterChest", Preference.Most)]
]));
Check(directions.Groups[0].Rules.Select(rule => rule.Preference).SequenceEqual([Preference.Most, Preference.Fewest, Preference.Fewest, Preference.Most]), "legacy scoped presence and absence map to average direction");
Reject(() => Settings.Parse("{}"), "reject malformed settings");
Reject(() => Settings.Parse("[]"), "reject non-object settings");
Reject(() => Settings.Parse(migrated.Replace("\"Version\": 4", "\"Version\": 5")), "reject unknown settings version");
Reject(() => Settings.Parse(migrated.Replace("Most", "NoSuchPreference")), "reject unknown preference");
Reject(() => Settings.Parse(migrated.Replace("AverageFloor", "InvalidMeasure")), "reject unknown measure");
Reject(() => Settings.Parse(migrated.Replace("Most", "Present")), "reject presence for averages");
Reject(() => Settings.Parse(Legacy(3, [[(Room.Shop, "InvalidSegment", Preference.Most)]])), "reject malformed old scope");

var graph = new Dictionary<int, MapNode>
{
    [0] = new(0, Room.Shop, 0, [1, 2]),
    [1] = new(1, Room.Shop, 1, [3]),
    [2] = new(2, Room.RestSite, 1, [3]),
    [3] = new(3, Room.Boss, 2, [])
};
Rule[] presence = [new(Room.Shop, Measure.Count, Preference.Present), new(Room.RestSite, Measure.Count, Preference.Most)];
var plan = Planner.Solve(graph, [0], [3], presence);
Check(plan.Nodes.SetEquals([0, 2, 3]), "satisfied presence allows a later priority to decide");
plan = Planner.Solve(graph, [0], [3], [new(Room.Shop, Measure.Count, Preference.Absent)]);
Check(plan.Nodes.SetEquals([0, 1, 2, 3]), "absence penalty applies once and retains all ties");
plan = Planner.Solve(graph, [0], [3], [new(Room.Shop, Measure.Count, Preference.Fewest)]);
Check(plan.Nodes.SetEquals([0, 2, 3]), "fewest differs from absence");
plan = Planner.Solve(graph, [1, 2], [3], presence);
Check(plan.Nodes.SetEquals([1, 3]), "replanning ignores previously visited rooms");
plan = Planner.Solve(graph, [], [3], presence);
Check(plan.Nodes.Count == 0, "completed act has no future route");
graph[4] = new(4, Room.RestSite, 1, []);
plan = Planner.Solve(graph, [0, 4], [3], [new(Room.RestSite, Measure.Count, Preference.Most)]);
Check(!plan.Nodes.Contains(4), "dead ends cannot beat a route to the boss");
foreach (var preference in new[] { Preference.Most, Preference.Fewest })
{
    plan = Planner.Solve(graph, [1, 2], [3], [new(Room.Shop, Measure.AverageFloor, preference)]);
    Check(plan.Nodes.SetEquals([1, 3]), "zero matching rooms rank behind a defined average " + preference);
    plan = Planner.Solve(graph, [0, 2], [3], [new(Room.Shop, Measure.AverageFloor, preference)]);
    Check(plan.Nodes.Contains(0), "a room on floor zero has a defined average " + preference);
}
plan = Planner.Solve(graph, [0], [3], [new(Room.Elite, Measure.AverageFloor, Preference.Fewest), new(Room.RestSite, Measure.Count, Preference.Most)]);
Check(plan.Nodes.SetEquals([0, 2, 3]), "all routes with zero matching rooms defer to the next priority");
plan = Planner.Solve(graph, [1, 2], [3], [new(Room.Shop, Measure.Count, Preference.Fewest), new(Room.Shop, Measure.AverageFloor, Preference.Most)]);
Check(plan.Nodes.SetEquals([2, 3]), "zero count wins when explicitly preferred before an average");
graph[4] = new(4, Room.Monster, 1, [4]);
Reject(() => Planner.Solve(graph, [4], [3], []), "reject cyclic maps");
Reject(() => Planner.Solve(graph, [99], [3], []), "reject missing map nodes");
Reject(() => Planner.Solve(graph, [0], [3], Enumerable.Repeat(presence[0], Settings.MaxRules + 1).ToArray()), "reject excessive priorities");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    Reject(() => Planner.Solve(graph, [0], [3], presence, cancelled.Token), "cancel stale planning");
}
var averages = new Dictionary<int, MapNode>
{
    [0] = new(0, Room.Elite, 1, [1, 2]),
    [1] = new(1, Room.Elite, 9, [4]),
    [2] = new(2, Room.Elite, 7, [3]),
    [3] = new(3, Room.Elite, 8, [4]),
    [4] = new(4, Room.Boss, 10, [])
};
plan = Planner.Solve(averages, [0], [4], [new(Room.Elite, Measure.AverageFloor, Preference.Fewest)]);
Check(plan.Nodes.SetEquals([0, 1, 4]), "average compares complete routes with unequal counts and a common prefix");
plan = Planner.Solve(averages, [1, 2], [4], [new(Room.Elite, Measure.AverageFloor, Preference.Fewest)]);
Check(plan.Nodes.SetEquals([2, 3, 4]), "average recalculates from the remaining route");
averages[1] = averages[1] with { Floor = 5 };
averages[2] = averages[2] with { Floor = 3 };
averages[3] = averages[3] with { Floor = 5 };
plan = Planner.Solve(averages, [0], [4], [new(Room.Elite, Measure.AverageFloor, Preference.Most)]);
Check(plan.Nodes.SetEquals([0, 1, 2, 3, 4]), "equal averages retain all routes with different counts");
plan = Planner.Solve(averages, [0], [4], [new(Room.Elite, Measure.AverageFloor, Preference.Most), new(Room.Elite, Measure.Count, Preference.Most)]);
Check(plan.Nodes.SetEquals([0, 2, 3, 4]), "later priority breaks equal average ties exactly");

var timer = Stopwatch.StartNew();
var dense = Enumerable.Range(0, 144).ToDictionary(id => id, id => new MapNode(id,
    id >= 135 ? Room.Boss : Room.RestSite, id / 9,
    id >= 135 ? [] : Enumerable.Range((id / 9 + 1) * 9, 9).ToArray()));
var densePlan = Planner.Solve(dense, Enumerable.Range(0, 9).ToArray(), Enumerable.Range(135, 9).ToArray(),
    [new(Room.RestSite, Measure.AverageFloor, Preference.Most)]);
Check(densePlan.Nodes.Count == 144 && densePlan.Edges.Count == 1215, "dense map retains all tied averages without enumerating paths");
foreach (int id in dense.Keys.ToArray()) if (id < 135) dense[id] = dense[id] with { Room = (Room)(id % 6) };
var varied = Planner.Solve(dense, Enumerable.Range(0, 9).ToArray(), Enumerable.Range(135, 9).ToArray(),
    Enumerable.Range(0, 6).SelectMany(i => new Rule[] { new((Room)i, Measure.AverageFloor, Preference.Most), new((Room)i, Measure.Count, Preference.Present) }).ToArray());
Check(varied.Nodes.Count > 0 && varied.Nodes.Any(id => dense[id].Room == Room.Boss), "dense mixed map supports twelve priorities");
Console.WriteLine($"Dense planning: {timer.ElapsedMilliseconds} ms");
var random = new Random(50721);
for (int sample = 0; sample < 1500; sample++)
{
    var map = new Dictionary<int, MapNode>();
    int layers = random.Next(3, 8);
    for (int layer = 0; layer < layers; layer++)
    {
        for (int column = 0; column < 3; column++)
        {
            int id = layer * 3 + column;
            var room = layer == layers - 1 ? Room.Boss : (Room)random.Next(6);
            int[] children = layer == layers - 1 ? [] : Enumerable.Range((layer + 1) * 3, 3).Where(_ => random.Next(2) == 0).ToArray();
            map[id] = new(id, room, layer, children);
        }
    }
    Rule[] rules = Enumerable.Range(0, random.Next(0, 13)).Select(_ =>
    {
        var measure = (Measure)random.Next(2);
        return new Rule((Room)random.Next(6), measure, (Preference)random.Next(measure == Measure.AverageFloor ? 2 : 4));
    }).ToArray();
    int[] starts = random.Next(2) == 0 ? [0, 1, 2] : [3, 4, 5];
    int[] goals = [(layers - 1) * 3, (layers - 1) * 3 + 1, (layers - 1) * 3 + 2];
    var actual = Planner.Solve(map, starts, goals, rules);
    var expected = Oracle(map, starts, goals, rules);
    Check(actual.Nodes.SetEquals(expected.Nodes) && actual.Edges.SetEquals(expected.Edges), $"exhaustive comparison {sample}");
}

string directory = Path.Combine(Path.GetTempPath(), "router-tests-" + Guid.NewGuid());
try
{
    string file = Path.Combine(directory, "settings.json");
    defaults.Save(file);
    defaults.Groups[4].Enabled = true;
    defaults.Save(file);
    Check(Settings.Parse(File.ReadAllText(file)).Groups[4].Enabled && !File.Exists(file + ".tmp"), "atomic settings replacement");
}
finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
Console.WriteLine($"PASS {checks} planning and settings checks");
