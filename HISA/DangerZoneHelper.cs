using System;
using System.Collections.Generic;
using System.Linq;
using HISA.EVEData;

namespace HISA
{
    public static class DangerZoneHelper
    {
        public static HashSet<string> GetDangerZoneSystems(MapConfig mapConf, EveManager eveManager)
        {
            HashSet<string> systems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if(mapConf == null || eveManager?.LocalCharacters == null)
            {
                return systems;
            }

            int jumpRange = mapConf.DangerZoneRange < 1 ? 1 : mapConf.DangerZoneRange;
            IEnumerable<LocalCharacter> targetCharacters = GetTargetCharacters(mapConf, eveManager);

            foreach(LocalCharacter character in targetCharacters)
            {
                if(string.IsNullOrWhiteSpace(character.Location))
                {
                    continue;
                }

                List<string> warningSystems = Navigation.GetSystemsXJumpsFrom(new List<string>(), character.Location, jumpRange);
                if(warningSystems == null)
                {
                    continue;
                }

                foreach(string systemName in warningSystems)
                {
                    systems.Add(systemName);
                }
            }

            return systems;
        }

        private static IEnumerable<LocalCharacter> GetTargetCharacters(MapConfig mapConf, EveManager eveManager)
        {
            string selectedCharacter = mapConf.DangerZoneCharacter?.Trim() ?? string.Empty;

            IEnumerable<LocalCharacter> onlineCharacters = eveManager.LocalCharacters
                .Where(c => c != null && c.IsOnline && !string.IsNullOrWhiteSpace(c.Location));

            if(string.IsNullOrWhiteSpace(selectedCharacter))
            {
                return onlineCharacters;
            }

            return onlineCharacters.Where(c => string.Equals(c.Name, selectedCharacter, StringComparison.OrdinalIgnoreCase));
        }
    }
}
