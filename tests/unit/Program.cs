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

RoutePlan Oracle(Dictionary<int, MapNode> map, int[] starts, Rule[] rules, bool afterChest)
{
    int[]? best = null;
    var winners = new List<int[]>();
    void Walk(int id, List<int> path)
    {
        path.Add(id);
        if (map[id].Room == Room.Boss)
        {
            var counts = new int[rules.Length];
            bool after = afterChest;
            foreach (int step in path)
            {
                var room = map[step].Room;
                for (int i = 0; i < rules.Length; i++)
                {
                    var rule = rules[i];
                    bool segment = rule.Segment == Segment.WholeAct || room != Room.Treasure &&
                        (rule.Segment == Segment.AfterChest ? after : !after);
                    if (rule.Room == room && segment) counts[i]++;
                }
                if (room == Room.Treasure) after = true;
            }
            var score = counts.Select((count, i) => rules[i].Preference switch
            {
                Preference.Most => count,
                Preference.Fewest => -count,
                Preference.Present => count > 0 ? 1 : 0,
                _ => count > 0 ? -1 : 0
            }).ToArray();
            int compare = best == null ? 1 : Planner.Compare(score, best);
            if (compare > 0) { best = score; winners.Clear(); }
            if (compare >= 0) winners.Add(path.ToArray());
        }
        else foreach (int child in map[id].Children) Walk(child, path);
        path.RemoveAt(path.Count - 1);
    }
    foreach (int start in starts) Walk(start, []);
    return new RoutePlan(best ?? new int[rules.Length], winners.SelectMany(p => p).ToHashSet(),
        winners.SelectMany(p => p.Zip(p.Skip(1), (from, to) => new Edge(from, to))).ToHashSet());
}

var defaults = Settings.Defaults();
Check(defaults.Groups.Count == 5 && defaults.Groups.Count(g => g.Enabled) == 2, "five groups with two enabled presets");
Check(defaults.Groups[0].Rules[0] == new Rule(Room.Elite, Segment.WholeAct, Preference.Most), "aggressive first");
Check(defaults.Groups[1].Rules[0] == new Rule(Room.RestSite, Segment.WholeAct, Preference.Most), "steady second");
Check(Settings.Parse(JsonSerializer.Serialize(defaults, Settings.Json)).Groups[1].Rules.SequenceEqual(defaults.Groups[1].Rules), "priority settings roundtrip");
var legacy = Settings.Parse(JsonSerializer.Serialize(defaults, Settings.Json).Replace("\"Enabled\": true", "\"Name\": \"Old label\", \"Enabled\": true"));
Check(legacy.Groups[0].Rules.SequenceEqual(defaults.Groups[0].Rules) && !JsonSerializer.Serialize(legacy, Settings.Json).Contains("Name", StringComparison.Ordinal), "existing named configurations retain priorities and drop their labels");
Reject(() => Settings.Parse("{}"), "reject malformed settings");
Reject(() => Settings.Parse("{\"Version\":2}"), "reject unknown settings version");
Reject(() => Settings.Parse(JsonSerializer.Serialize(defaults, Settings.Json).Replace("Most", "NoSuchPreference")), "reject unknown rules");
var graph = new Dictionary<int, MapNode>
{
    [0] = new(0, Room.Shop, [1, 2]),
    [1] = new(1, Room.Shop, [3]),
    [2] = new(2, Room.RestSite, [3]),
    [3] = new(3, Room.Boss, [])
};
Rule[] presence = [new(Room.Shop, Segment.WholeAct, Preference.Present), new(Room.RestSite, Segment.WholeAct, Preference.Most)];
var plan = Planner.Solve(graph, [0], [3], presence, false);
Check(plan.Nodes.SetEquals([0, 2, 3]), "satisfied presence must allow a later priority to decide");
plan = Planner.Solve(graph, [0], [3], [new(Room.Shop, Segment.WholeAct, Preference.Absent)], false);
Check(plan.Nodes.SetEquals([0, 1, 2, 3]), "absence penalty applies once and retains all ties");
plan = Planner.Solve(graph, [0], [3], [new(Room.Shop, Segment.WholeAct, Preference.Fewest)], false);
Check(plan.Nodes.SetEquals([0, 2, 3]), "fewest differs from absence");
plan = Planner.Solve(graph, [1, 2], [3], presence, true);
Check(plan.Nodes.SetEquals([1, 3]), "replanning ignores previously visited rooms");
plan = Planner.Solve(graph, [], [3], presence, false);
Check(plan.Nodes.Count == 0, "completed act has no future route");
graph[4] = new(4, Room.RestSite, []);
plan = Planner.Solve(graph, [0, 4], [3], [new(Room.RestSite, Segment.WholeAct, Preference.Most)], false);
Check(!plan.Nodes.Contains(4), "dead ends cannot beat a route to the boss");
graph[4] = new(4, Room.Monster, [4]);
Reject(() => Planner.Solve(graph, [4], [3], [], false), "reject cyclic maps");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    Reject(() => Planner.Solve(graph, [0], [3], presence, false, cancelled.Token), "cancel stale planning");
}

var random = new Random(50721);
var dense = Enumerable.Range(0, 144).ToDictionary(id => id, id => new MapNode(id,
    id >= 135 ? Room.Boss : id / 9 == 8 ? Room.Treasure : Room.RestSite,
    id >= 135 ? [] : Enumerable.Range((id / 9 + 1) * 9, 9).ToArray()));
var densePlan = Planner.Solve(dense, Enumerable.Range(0, 9).ToArray(), Enumerable.Range(135, 9).ToArray(),
    [new(Room.RestSite, Segment.AfterChest, Preference.Most)], false);
Check(densePlan.Nodes.Count == 144 && densePlan.Edges.Count == 1215 && densePlan.Score[0] == 6, "dense map retains all optimal branches without enumerating paths");
for (int sample = 0; sample < 750; sample++)
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
            map[id] = new(id, room, children);
        }
    }
    Rule[] rules = Enumerable.Range(0, random.Next(1, 9)).Select(_ => new Rule((Room)random.Next(6), (Segment)random.Next(3), (Preference)random.Next(4))).ToArray();
    bool after = random.Next(2) == 0;
    int[] starts = random.Next(2) == 0 ? [0, 1, 2] : [3, 4, 5];
    var actual = Planner.Solve(map, starts, [(layers - 1) * 3, (layers - 1) * 3 + 1, (layers - 1) * 3 + 2], rules, after);
    var expected = Oracle(map, starts, rules, after);
    Check(actual.Score.SequenceEqual(expected.Score) && actual.Nodes.SetEquals(expected.Nodes) && actual.Edges.SetEquals(expected.Edges), $"exhaustive comparison {sample}");
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
