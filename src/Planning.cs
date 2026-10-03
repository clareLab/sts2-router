namespace router;

internal enum Room { Monster, Elite, RestSite, Shop, Unknown, Treasure, Boss, Ancient }
internal enum Segment { WholeAct, BeforeChest, AfterChest }
internal enum Preference { Most, Fewest, Present, Absent }
internal sealed record Rule(Room Room, Segment Segment, Preference Preference);
internal readonly record struct Edge(int From, int To);
internal sealed record MapNode(int Id, Room Room, int[] Children);
internal sealed record RoutePlan(int[] Score, HashSet<int> Nodes, HashSet<Edge> Edges);

internal static class Planner
{
    private readonly record struct State(int Node, bool AfterChest, int Seen);
    private sealed record Choice(int[] Score, State[] Next);

    internal static RoutePlan Solve(IReadOnlyDictionary<int, MapNode> map, int[] starts, int[] goals,
        IReadOnlyList<Rule> rules, bool afterChest, CancellationToken cancellation = default)
    {
        if (rules.Count > Settings.MaxRules) throw new ArgumentException("Too many priorities.", nameof(rules));
        var memo = new Dictionary<State, Choice?>();
        var visiting = new HashSet<int>();
        var destinations = goals.ToHashSet();
        int visits = 0;

        Choice? Visit(State state)
        {
            cancellation.ThrowIfCancellationRequested();
            if (memo.TryGetValue(state, out var cached)) return cached;
            if (++visits > 200_000) throw new InvalidOperationException("Map planning limit reached.");
            if (!map.TryGetValue(state.Node, out var node)) throw new ArgumentException("Map contains a missing node.", nameof(map));
            if (!visiting.Add(state.Node)) throw new ArgumentException("Map contains a cycle.", nameof(map));
            var own = new int[rules.Count];
            int seen = state.Seen;
            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (node.Room != rule.Room || !Matches(rule.Segment, state.AfterChest, node.Room)) continue;
                bool presence = rule.Preference is Preference.Present or Preference.Absent;
                if (presence && (seen & (1 << i)) != 0) continue;
                own[i] = rule.Preference is Preference.Most or Preference.Present ? 1 : -1;
                if (presence) seen |= 1 << i;
            }
            Choice? result = null;
            if (destinations.Contains(node.Id)) result = new Choice(own, []);
            else
            {
                int[]? best = null;
                var next = new List<State>();
                foreach (int child in node.Children.Distinct().Order())
                {
                    var childState = new State(child, state.AfterChest || node.Room == Room.Treasure, seen);
                    var candidate = Visit(childState);
                    if (candidate == null) continue;
                    int comparison = best == null ? 1 : Compare(candidate.Score, best);
                    if (comparison > 0) { best = candidate.Score; next.Clear(); }
                    if (comparison >= 0) next.Add(childState);
                }
                if (best != null) result = new Choice(own.Zip(best, (a, b) => a + b).ToArray(), next.ToArray());
            }
            visiting.Remove(state.Node);
            memo[state] = result;
            return result;
        }

        int[]? score = null;
        var roots = new List<State>();
        foreach (int start in starts.Distinct().Order())
        {
            var state = new State(start, afterChest, 0);
            var candidate = Visit(state);
            if (candidate == null) continue;
            int comparison = score == null ? 1 : Compare(candidate.Score, score);
            if (comparison > 0) { score = candidate.Score; roots.Clear(); }
            if (comparison >= 0) roots.Add(state);
        }
        var nodes = new HashSet<int>();
        var edges = new HashSet<Edge>();
        var walked = new HashSet<State>();
        var pending = new Stack<State>(roots);
        while (pending.TryPop(out var state))
        {
            if (!walked.Add(state)) continue;
            nodes.Add(state.Node);
            foreach (var child in memo[state]!.Next)
            {
                edges.Add(new Edge(state.Node, child.Node));
                pending.Push(child);
            }
        }
        return new RoutePlan(score ?? new int[rules.Count], nodes, edges);
    }

    internal static bool Matches(Segment segment, bool afterChest, Room room) => segment == Segment.WholeAct ||
        room != Room.Treasure && (segment == Segment.AfterChest) == afterChest;

    internal static int Compare(IReadOnlyList<int> left, IReadOnlyList<int> right)
    {
        for (int i = 0; i < left.Count; i++)
        {
            int comparison = left[i].CompareTo(right[i]);
            if (comparison != 0) return comparison;
        }
        return 0;
    }
}
