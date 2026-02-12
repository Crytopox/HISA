using System.Collections.Generic;
using System.Linq;
using HISA.EVEData;

namespace HISA.Helpers;

public sealed class IntelAlertDecision
{
    public bool HasKnownSystem { get; init; }
    public bool ShouldPlaySound { get; init; }
    public bool ShouldFlashWindow { get; init; }
}

public static class IntelAlertDecisionHelper
{
    public static IntelAlertDecision Evaluate(
        IntelData intelData,
        MapConfig mapConfig,
        IEnumerable<string> intelAlertFilters,
        HashSet<string> warningSystems)
    {
        if(intelData == null || mapConfig == null)
        {
            return new IntelAlertDecision();
        }

        bool hasKnownSystem = intelData.Systems != null && intelData.Systems.Count > 0;
        bool playSound = false;
        bool flashWindow = false;

        if(mapConfig.PlayIntelSoundOnUnknown && !hasKnownSystem)
        {
            playSound = true;
            flashWindow = true;
        }

        if(mapConfig.PlayIntelSound || mapConfig.FlashWindow || mapConfig.PlayIntelSoundOnAlert)
        {
            if((mapConfig.PlaySoundOnlyInDangerZone || mapConfig.FlashWindowOnlyInDangerZone) &&
               warningSystems != null &&
               intelData.Systems != null)
            {
                foreach(string systemName in intelData.Systems)
                {
                    if(warningSystems.Contains(systemName))
                    {
                        playSound = playSound || mapConfig.PlaySoundOnlyInDangerZone;
                        flashWindow = flashWindow || mapConfig.FlashWindowOnlyInDangerZone;
                        break;
                    }
                }
            }

            if(mapConfig.PlayIntelSoundOnAlert &&
               !string.IsNullOrWhiteSpace(intelData.RawIntelString) &&
               intelAlertFilters != null)
            {
                if(intelAlertFilters.Any(alertName => intelData.RawIntelString.Contains(alertName)))
                {
                    playSound = true;
                }
            }
        }

        bool shouldPlaySound = hasKnownSystem && (playSound || (!mapConfig.PlaySoundOnlyInDangerZone && mapConfig.PlayIntelSound));
        bool shouldFlashWindow = flashWindow || (!mapConfig.FlashWindowOnlyInDangerZone && mapConfig.FlashWindow);

        return new IntelAlertDecision
        {
            HasKnownSystem = hasKnownSystem,
            ShouldPlaySound = shouldPlaySound,
            ShouldFlashWindow = shouldFlashWindow
        };
    }
}
