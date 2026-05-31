using Hisa.Core.Models;

namespace Hisa.Core.Abstractions;

public interface IRouteDistanceService
{
    RoutingPathResult FindShortestPath(RoutingRequest request);
    IReadOnlyDictionary<long, double> ComputeDistances(RoutingDistancesRequest request);
}
