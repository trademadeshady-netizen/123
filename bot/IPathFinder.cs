using HerbBot.Math;
namespace HerbBot.Interfaces;

public interface IPathFinder
{
    List<Vector3> FindPath(Vector3 start, Vector3 end);
    /// <summary>Маршрут для полёта: препятствия обходятся набором высоты.</summary>
    List<Vector3> FindFlyRoute(Vector3 start, Vector3 end);
}
