using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace router;

internal sealed record MapSnapshot(Dictionary<int, MapNode> Nodes, Dictionary<int, Vector2> Positions,
    Dictionary<int, float> Radii, int[] Starts, int[] Goals, int? Current, bool AfterChest)
{
    internal static MapSnapshot Capture(NMapScreen screen, RunState run)
    {
        var points = screen.GetNode<Control>("TheMap/Points").GetChildren().OfType<NMapPoint>().Where(point => !point.IsQueuedForDeletion()).ToArray();
        var index = points.Select((point, i) => (point.Point.coord, i)).ToDictionary(pair => pair.coord, pair => pair.i);
        var nodes = new Dictionary<int, MapNode>();
        var positions = new Dictionary<int, Vector2>();
        var radii = new Dictionary<int, float>();
        foreach (var point in points)
        {
            int id = index[point.Point.coord];
            var children = point.Point.Children.Where(child => index.ContainsKey(child.coord)).Select(child => index[child.coord]).ToArray();
            nodes[id] = new MapNode(id, Convert(point.Point.PointType), children);
            positions[id] = point.GetTransform() * (point.Size * 0.5f);
            radii[id] = point is NNormalMapPoint ? 34 : Math.Max(42, Math.Min(point.Size.X, point.Size.Y) * 0.32f);
        }
        int? current = run.CurrentMapCoord is { } coord && index.TryGetValue(coord, out int value) ? value : null;
        var next = run.CurrentMapPoint is { } currentPoint
            ? MapTravel.GetTravelablePointsFrom(run, currentPoint).Where(p => index.ContainsKey(p.coord)).Select(p => index[p.coord]).ToArray()
            : [index[run.Map.StartingMapPoint.coord]];
        var goal = run.Map.SecondBossMapPoint ?? run.Map.BossMapPoint;
        bool afterChest = run.VisitedMapCoords.Any(c => run.Map.GetPoint(c)?.PointType == MapPointType.Treasure);
        return new MapSnapshot(nodes, positions, radii, next, [index[goal.coord]], current, afterChest);
    }

    private static Room Convert(MapPointType type) => type switch
    {
        MapPointType.Monster => Room.Monster,
        MapPointType.Elite => Room.Elite,
        MapPointType.RestSite => Room.RestSite,
        MapPointType.Shop => Room.Shop,
        MapPointType.Treasure => Room.Treasure,
        MapPointType.Boss => Room.Boss,
        MapPointType.Ancient => Room.Ancient,
        _ => Room.Unknown
    };
}

internal partial class RouteInk : Node2D
{
    private MapSnapshot? _map;
    private readonly Dictionary<Edge, Color> _edges = [];
    private readonly Dictionary<int, Color> _nodes = [];

    internal RouteInk() => Draw += Paint;

    internal void SetRoutes(MapSnapshot map, (int Group, RoutePlan Plan)[] plans)
    {
        _map = map;
        _edges.Clear();
        _nodes.Clear();
        foreach (var (group, plan) in plans)
        {
            var color = Ui.RouteColors[group];
            foreach (var edge in plan.Edges) Blend(_edges, edge, color);
            foreach (int id in plan.Nodes) Blend(_nodes, id, color);
            if (map.Current is { } current)
            {
                foreach (int start in map.Starts.Where(plan.Nodes.Contains)) Blend(_edges, new Edge(current, start), color);
                if (plan.Nodes.Count > 0) Blend(_nodes, current, color);
            }
        }
        QueueRedraw();
    }

    private static void Blend<T>(Dictionary<T, Color> colors, T key, Color color) where T : notnull
    {
        colors[key] = colors.TryGetValue(key, out var previous)
            ? new Color(Math.Min(1, previous.R + color.R), Math.Min(1, previous.G + color.G), Math.Min(1, previous.B + color.B))
            : color;
    }

    private void Paint()
    {
        if (_map == null) return;
        foreach (var (edge, color) in _edges)
        {
            var from = _map.Positions[edge.From];
            var to = _map.Positions[edge.To];
            var direction = (to - from).Normalized();
            from += direction * _map.Radii[edge.From];
            to -= direction * _map.Radii[edge.To];
            DrawLine(from, to, new Color("f0dfa366"), 13, true);
            DrawLine(from, to, new Color(color, 0.88f), 7, true);
        }
        foreach (var (id, color) in _nodes)
        {
            var position = _map.Positions[id];
            float radius = _map.Radii[id];
            DrawCircle(position, radius, new Color(color, 0.13f));
            DrawArc(position, radius, 0, Mathf.Tau, 64, new Color("f0dfa380"), 9, true);
            DrawArc(position, radius, 0, Mathf.Tau, 64, color, 4, true);
        }
    }
}
