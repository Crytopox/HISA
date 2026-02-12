using System;
using System.Linq;
using HISA.EVEData;

namespace HISA.Helpers;

public static class RegionStartupResolver
{
    public static string GetStartupRegionName(EveManager eveManager, string lastRegionsViewRegion, string defaultRegion)
    {
        string preferredRegion = GetPreferredRegionName(lastRegionsViewRegion, defaultRegion);
        string resolvedPreferredRegion = ResolveKnownRegionName(eveManager, preferredRegion);
        if(!string.IsNullOrWhiteSpace(resolvedPreferredRegion))
        {
            return resolvedPreferredRegion;
        }

        return eveManager?.Regions?.FirstOrDefault()?.Name;
    }

    public static string GetPreferredRegionName(string lastRegionsViewRegion, string defaultRegion)
    {
        string lastRegion = NormalizeRegionName(lastRegionsViewRegion);
        if(!string.IsNullOrWhiteSpace(lastRegion))
        {
            return lastRegion;
        }

        return NormalizeRegionName(defaultRegion);
    }

    public static string ResolveKnownRegionName(EveManager eveManager, string regionName)
    {
        if(string.IsNullOrWhiteSpace(regionName) || eveManager?.Regions == null)
        {
            return null;
        }

        string candidate = regionName.Trim();
        if(candidate.Length == 0)
        {
            return null;
        }

        MapRegion exact = eveManager.GetRegion(candidate);
        if(exact != null)
        {
            return exact.Name;
        }

        MapRegion caseInsensitive = eveManager.Regions.FirstOrDefault(r => string.Equals(r.Name, candidate, StringComparison.OrdinalIgnoreCase));
        return caseInsensitive?.Name;
    }

    public static string NormalizeRegionName(string regionName)
    {
        if(string.IsNullOrWhiteSpace(regionName))
        {
            return null;
        }

        string trimmed = regionName.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
