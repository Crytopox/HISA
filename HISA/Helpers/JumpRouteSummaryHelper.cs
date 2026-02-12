using HISA.EVEData;

namespace HISA.Helpers;

public static class JumpRouteSummaryHelper
{
    public static string BuildSummaryText(JumpRoute route)
    {
        if(route == null || route.CurrentRoute == null || route.CurrentRoute.Count == 0)
        {
            return "No Valid Route Found";
        }

        return $"{route.CurrentRoute.Count - 2} Mids";
    }
}
