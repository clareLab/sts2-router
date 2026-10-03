namespace router;

internal enum Room { Monster, Elite, RestSite, Shop, Unknown, Treasure, Boss, Ancient }
internal enum Measure { Count, AverageFloor }
internal enum Preference { Most, Fewest, Present, Absent }
internal sealed record Rule(Room Room, Measure Measure, Preference Preference);
internal readonly record struct Edge(int From, int To);
internal sealed record MapNode(int Id, Room Room, int Floor, int[] Children);
internal sealed record RoutePlan(HashSet<int> Nodes, HashSet<Edge> Edges);

internal static class Planner
{
    private readonly record struct State(int Node, int Seen);
    private sealed record Choice(long Score, long Total, int Count, State[] Next);
    private sealed record Ranking(Dictionary<State, Choice> Choices, State[] Roots, Choice Best);

    internal static RoutePlan Solve(IReadOnlyDictionary<int, MapNode> map, int[] starts, int[] goals,
        IReadOnlyList<Rule> rules, CancellationToken cancellation = default)
    {
        if (rules.Count > Settings.MaxRules) throw new ArgumentException("Too many priorities.", nameof(rules));
        var graph = new Dictionary<State, State[]>();
        var order = new List<State>();
        var visiting = new HashSet<int>();
        var destinations = goals.ToHashSet();
        int tracked = rules.Where(rule => rule.Measure == Measure.AverageFloor || rule.Preference is Preference.Present or Preference.Absent)
            .Aggregate(0, (mask, rule) => mask | 1 << (int)rule.Room);

        State Build(int id, int seen)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!map.TryGetValue(id, out var node)) throw new ArgumentException("Map contains a missing node.", nameof(map));
            if (!visiting.Add(id)) throw new ArgumentException("Map contains a cycle.", nameof(map));
            var state = new State(id, seen | (tracked & (1 << (int)node.Room)));
            if (!graph.ContainsKey(state))
            {
                if (graph.Count >= 200_000) throw new InvalidOperationException("Map planning limit reached.");
                graph[state] = [];
                if (!destinations.Contains(id))
                    graph[state] = node.Children.Distinct().Order().Select(child => Build(child, state.Seen)).ToArray();
                order.Add(state);
            }
            visiting.Remove(id);
            return state;
        }

        var roots = starts.Distinct().Order().Select(id => Build(id, 0)).ToArray();

        Ranking? Rank(Rule? rule, long numerator = 0, int denominator = 1)
        {
            var choices = new Dictionary<State, Choice>();
            bool average = rule?.Measure == Measure.AverageFloor;
            bool presence = rule?.Preference is Preference.Present or Preference.Absent;
            int sign = rule?.Preference is Preference.Fewest or Preference.Absent ? -1 : 1;
            foreach (var state in order)
            {
                cancellation.ThrowIfCancellationRequested();
                var node = map[state.Node];
                bool match = rule != null && node.Room == rule.Room;
                long own = !match || presence ? 0 : average ? checked(sign * (long)node.Floor * denominator - numerator) : sign;
                long total = match ? node.Floor : 0;
                int count = match ? 1 : 0;
                if (destinations.Contains(node.Id))
                {
                    bool found = rule != null && (state.Seen & (1 << (int)rule.Room)) != 0;
                    if (!average || found)
                        choices[state] = new Choice(own + (presence && found ? sign : 0), total, count, []);
                    continue;
                }
                Choice? best = null;
                var next = new List<State>();
                foreach (var child in graph[state])
                {
                    if (!choices.TryGetValue(child, out var candidate)) continue;
                    int comparison = best == null ? 1 : candidate.Score.CompareTo(best.Score);
                    if (comparison > 0) { best = candidate; next.Clear(); }
                    if (comparison >= 0) next.Add(child);
                }
                if (best != null) choices[state] = new Choice(checked(own + best.Score), total + best.Total, count + best.Count, next.ToArray());
            }
            var candidates = roots.Where(choices.ContainsKey).ToArray();
            if (candidates.Length == 0) return null;
            long score = candidates.Max(state => choices[state].Score);
            var winners = candidates.Where(state => choices[state].Score == score).ToArray();
            return new Ranking(choices, winners, choices[winners[0]]);
        }

        void Keep(Ranking ranking)
        {
            roots = ranking.Roots;
            var reachable = new HashSet<State>();
            var pending = new Stack<State>(roots);
            while (pending.TryPop(out var state))
            {
                if (!reachable.Add(state)) continue;
                graph[state] = ranking.Choices[state].Next;
                foreach (var child in graph[state]) pending.Push(child);
            }
            order.RemoveAll(state => !reachable.Contains(state));
        }

        if (Rank(null) is { } complete) Keep(complete);
        else return new RoutePlan([], []);
        foreach (var rule in rules)
        {
            var ranking = Rank(rule);
            if (ranking == null) continue;
            if (rule.Measure == Measure.AverageFloor)
            {
                int sign = rule.Preference == Preference.Fewest ? -1 : 1;
                while (true)
                {
                    long numerator = sign * ranking.Best.Total;
                    int denominator = ranking.Best.Count;
                    ranking = Rank(rule, numerator, denominator)!;
                    if (ranking.Best.Score == 0) break;
                }
            }
            Keep(ranking);
        }
        return new RoutePlan(order.Select(state => state.Node).ToHashSet(),
            order.SelectMany(state => graph[state].Select(child => new Edge(state.Node, child.Node))).ToHashSet());
    }
}
