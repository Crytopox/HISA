using System.Collections.Generic;
using HISA.EVEData;

namespace HISA.Helpers;

public static class IntelCacheHelper
{
    public static void SyncRecent(IList<IntelData> intelCache, IList<IntelData> latestIntel, int maxItems)
    {
        if(intelCache == null || latestIntel == null || maxItems < 1)
        {
            return;
        }

        List<IntelData> removeList = new List<IntelData>();

        if(intelCache.Count >= maxItems)
        {
            foreach(IntelData intelData in intelCache)
            {
                if(!latestIntel.Contains(intelData))
                {
                    removeList.Add(intelData);
                }
            }

            foreach(IntelData intelData in removeList)
            {
                intelCache.Remove(intelData);
            }
        }

        foreach(IntelData intelData in latestIntel)
        {
            if(!intelCache.Contains(intelData))
            {
                intelCache.Insert(0, intelData);
            }
        }
    }
}
