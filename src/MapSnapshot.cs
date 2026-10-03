using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;

namespace router;

internal sealed record MapSnapshot(Dictionary<int, MapNode> Nodes, MapCoord[] Coordinates,
    int[] Starts, int[] Goals, int? Current, bool AfterChest)
{
    internal static MapSnapshot Capture(RunState run)
    {
        IEnumerable<MapPoint> all = run.Map.GetAllMapPoints().Append(run.Map.StartingMapPoint).Append(run.Map.BossMapPoint);
        if (run.Map.SecondBossMapPoint is { } secondBoss) all = all.Append(secondBoss);
        var points = all.DistinctBy(point => point.coord).ToArray();
        var index = points.Select((point, i) => (point.coord, i)).ToDictionary(pair => pair.coord, pair => pair.i);
        var nodes = new Dictionary<int, MapNode>();
        foreach (var point in points)
        {
            int id = index[point.coord];
            var children = point.Children.Where(child => index.ContainsKey(child.coord)).Select(child => index[child.coord]).ToArray();
            nodes[id] = new MapNode(id, Convert(point.PointType), children);
        }
        int? current = run.CurrentMapCoord is { } coord && index.TryGetValue(coord, out int value) ? value : null;
        var next = run.CurrentMapPoint is { } currentPoint
            ? MapTravel.GetTravelablePointsFrom(run, currentPoint).Where(p => index.ContainsKey(p.coord)).Select(p => index[p.coord]).ToArray()
            : [index[run.Map.StartingMapPoint.coord]];
        var goal = run.Map.SecondBossMapPoint ?? run.Map.BossMapPoint;
        bool afterChest = run.VisitedMapCoords.Any(c => run.Map.GetPoint(c)?.PointType == MapPointType.Treasure);
        return new MapSnapshot(nodes, points.Select(p => p.coord).ToArray(), next, [index[goal.coord]], current, afterChest);
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
