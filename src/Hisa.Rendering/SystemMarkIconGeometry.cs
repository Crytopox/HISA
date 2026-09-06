using Avalonia.Media;
using Hisa.Core.Models;

namespace Hisa.Rendering;

public static class SystemMarkIconGeometry
{
    private static readonly Dictionary<SystemMarkIconKind, StreamGeometry> Cache = [];

    public static StreamGeometry Get(SystemMarkIconKind kind)
    {
        if (Cache.TryGetValue(kind, out var cached))
        {
            return cached;
        }

        var geometry = StreamGeometry.Parse(GetPathData(kind));
        Cache[kind] = geometry;
        return geometry;
    }

    private static string GetPathData(SystemMarkIconKind kind) => kind switch
    {
        SystemMarkIconKind.Home =>
            "M2,8 L8,2 L14,8 L14,14 L10,14 L10,10 L6,10 L6,14 L2,14 Z",
        SystemMarkIconKind.Clone =>
            "M8,1.5 A3.2,3.2 0 1,0 8.01,1.5 Z M3.2,14.6 C3.2,10.8 5.4,8.6 8,8.6 C10.6,8.6 12.8,10.8 12.8,14.6 Z",
        SystemMarkIconKind.Industry =>
            "M2,13.5 L2,8.5 L5.5,8.5 L5.5,5.5 L9,5.5 L9,2.5 L14,2.5 L14,13.5 Z M3.3,11.8 L4.7,11.8 L4.7,13.5 L3.3,13.5 Z M6.3,11.8 L7.7,11.8 L7.7,13.5 L6.3,13.5 Z M9.3,11.8 L10.7,11.8 L10.7,13.5 L9.3,13.5 Z M12.3,11.8 L13.5,11.8 L13.5,13.5 L12.3,13.5 Z",
        SystemMarkIconKind.Market =>
            "M1.8,3.2 L4.4,3.2 L5.5,9.1 L12.8,9.1 L14.3,4.6 L6.1,4.6 L5.6,3.2 Z M6.1,12.1 A1.35,1.35 0 1,0 6.11,12.1 Z M12.2,12.1 A1.35,1.35 0 1,0 12.21,12.1 Z",
        SystemMarkIconKind.Staging =>
            "M3,14 L3,2 L12.5,5.6 L3,9.2 Z",
        SystemMarkIconKind.Mining =>
            "M2.2,13.8 L7.4,8.6 L9.2,10.4 L4,15.6 Z M8.6,7.4 L11.2,4.8 C12.4,3.6 14.2,3.8 14.8,5 C15.4,6.2 14.8,7.8 13.4,8.8 L10.6,10.4 Z",
        SystemMarkIconKind.Capital =>
            "M8,1.5 L9.8,5.8 L14.5,6.1 L10.8,9.2 L12,13.8 L8,11.5 L4,13.8 L5.2,9.2 L1.5,6.1 L6.2,5.8 Z",
        SystemMarkIconKind.Station =>
            "M3,14 L3,7 L6,7 L6,4 L10,4 L10,7 L13,7 L13,14 Z M5,10 L7,10 L7,14 L5,14 Z M9,10 L11,10 L11,14 L9,14 Z M7.2,2.2 L8,1.2 L8.8,2.2 Z",
        SystemMarkIconKind.Star =>
            "M8,1.8 L9.6,5.8 L14,6.2 L10.6,9.1 L11.7,13.4 L8,11.3 L4.3,13.4 L5.4,9.1 L2,6.2 L6.4,5.8 Z",
        SystemMarkIconKind.Flag =>
            "M3,14 L3,2 L12.5,4.8 L7.5,6.6 L12.5,8.4 L3,11.2 Z",
        SystemMarkIconKind.Shield =>
            "M8,1.5 L14,4.2 L14,8.4 C14,11.8 11.4,14.2 8,15 C4.6,14.2 2,11.8 2,8.4 L2,4.2 Z",
        SystemMarkIconKind.Crosshair =>
            "M7.25,1.2 L8.75,1.2 L8.75,4.3 L7.25,4.3 Z M7.25,11.7 L8.75,11.7 L8.75,14.8 L7.25,14.8 Z M1.2,7.25 L4.3,7.25 L4.3,8.75 L1.2,8.75 Z M11.7,7.25 L14.8,7.25 L14.8,8.75 L11.7,8.75 Z M8,5.15 A2.85,2.85 0 1,0 8.01,5.15 Z M8,6.45 A1.55,1.55 0 1,1 7.99,6.45 Z",
        SystemMarkIconKind.Warning =>
            "M8,1.5 L14.9,14.3 L1.1,14.3 Z M7.25,6.1 L8.75,6.1 L8.75,10.3 L7.25,10.3 Z M7.25,11.3 L8.75,11.3 L8.75,12.8 L7.25,12.8 Z",
        _ =>
            "M8,1.5 C5.6,1.5 3.6,3.6 3.6,6.2 C3.6,9.8 8,14.5 8,14.5 C8,14.5 12.4,9.8 12.4,6.2 C12.4,3.6 10.4,1.5 8,1.5 Z M8,8.1 A1.9,1.9 0 1,0 8.01,8.1 Z"
    };
}
