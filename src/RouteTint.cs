using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace router;

internal sealed class RouteTint
{
    private readonly Dictionary<TextureRect, (Color Original, Color Applied)> _colours = [];

    internal static IReadOnlyDictionary<(MapCoord, MapCoord), IReadOnlyList<TextureRect>> Paths(NMapScreen screen) =>
        AccessTools.Field(typeof(NMapScreen), "_paths")?.GetValue(screen) as IReadOnlyDictionary<(MapCoord, MapCoord), IReadOnlyList<TextureRect>>
        ?? throw new InvalidOperationException("Native map paths are unavailable.");

    internal void Apply(NMapScreen screen, MapSnapshot map, (int Group, RoutePlan Plan)[] plans)
    {
        Clear();
        var colours = new Dictionary<Edge, Color>();
        foreach (var (group, plan) in plans)
        {
            var edges = new HashSet<Edge>(plan.Edges);
            if (map.Current is { } current)
                foreach (int start in map.Starts.Where(plan.Nodes.Contains)) edges.Add(new Edge(current, start));
            foreach (var edge in edges)
            {
                var colour = Ui.RouteColors[group];
                colours[edge] = colours.TryGetValue(edge, out var previous)
                    ? new Color(Math.Min(1, previous.R + colour.R), Math.Min(1, previous.G + colour.G), Math.Min(1, previous.B + colour.B))
                    : colour;
            }
        }
        var paths = Paths(screen);
        foreach (var (edge, colour) in colours)
        {
            if (!paths.TryGetValue((map.Coordinates[edge.From], map.Coordinates[edge.To]), out var path)) continue;
            foreach (var tick in path)
            {
                if (!GodotObject.IsInstanceValid(tick) || tick.IsQueuedForDeletion()) continue;
                var applied = new Color(colour, tick.Modulate.A);
                _colours[tick] = (tick.Modulate, applied);
                tick.Modulate = applied;
            }
        }
    }

    internal void Clear()
    {
        foreach (var (tick, colours) in _colours)
            if (GodotObject.IsInstanceValid(tick) && !tick.IsQueuedForDeletion() && tick.Modulate == colours.Applied)
                tick.Modulate = colours.Original;
        _colours.Clear();
    }
}
