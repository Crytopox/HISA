using System;
using System.Collections.Generic;

namespace HISA
{
    public static class SovUpgradeIconCatalog
    {
        private static readonly Dictionary<string, string> IconPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Electric Stability Generator", "Images/SOV Upgrades/Electric Stability Generator.png" },
            { "Exotic Stability Generator", "Images/SOV Upgrades/Exotic Stability Generator.png" },
            { "Gamma Stability Generator", "Images/SOV Upgrades/Gamma Stability Generator.png" },
            { "Plasma Stability Generator", "Images/SOV Upgrades/Plasma Stability Generator.png" },

            { "Exploration Detector 1", "Images/SOV Upgrades/Exploration Detector 1.png" },
            { "Exploration Detector 2", "Images/SOV Upgrades/Exploration Detector 2.png" },
            { "Exploration Detector 3", "Images/SOV Upgrades/Exploration Detector 3.png" },

            { "Major Threat Detection Array 1", "Images/SOV Upgrades/Major Threat Detection Array 1.png" },
            { "Major Threat Detection Array 2", "Images/SOV Upgrades/Major Threat Detection Array 2.png" },
            { "Major Threat Detection Array 3", "Images/SOV Upgrades/Major Threat Detection Array 3.png" },

            { "Minor Threat Detection Array 1", "Images/SOV Upgrades/Minor Threat Detection Array 1.png" },
            { "Minor Threat Detection Array 2", "Images/SOV Upgrades/Minor Threat Detection Array 2.png" },
            { "Minor Threat Detection Array 3", "Images/SOV Upgrades/Minor Threat Detection Array 3.png" },

            { "Isogen Prospecting Array 1", "Images/SOV Upgrades/Isogen Prospecting Array 1.png" },
            { "Isogen Prospecting Array 2", "Images/SOV Upgrades/Isogen Prospecting Array 2.png" },
            { "Isogen Prospecting Array 3", "Images/SOV Upgrades/Isogen Prospecting Array 3.png" },

            { "Megacyte Prospecting Array 1", "Images/SOV Upgrades/Megacyte Prospecting Array 1.png" },
            { "Megacyte Prospecting Array 2", "Images/SOV Upgrades/Megacyte Prospecting Array 2.png" },
            { "Megacyte Prospecting Array 3", "Images/SOV Upgrades/Megacyte Prospecting Array 3.png" },

            { "Mexallon Prospecting Array 1", "Images/SOV Upgrades/Mexallon Prospecting Array 1.png" },
            { "Mexallon Prospecting Array 2", "Images/SOV Upgrades/Mexallon Prospecting Array 2.png" },
            { "Mexallon Prospecting Array 3", "Images/SOV Upgrades/Mexallon Prospecting Array 3.png" },

            { "Nocxium Prospecting Array 1", "Images/SOV Upgrades/Nocxium Prospecting Array 1.png" },
            { "Nocxium Prospecting Array 2", "Images/SOV Upgrades/Nocxium Prospecting Array 2.png" },
            { "Nocxium Prospecting Array 3", "Images/SOV Upgrades/Nocxium Prospecting Array 3.png" },

            { "Pyerite Prospecting Array 1", "Images/SOV Upgrades/Pyerite Prospecting Array 1.png" },
            { "Pyerite Prospecting Array 2", "Images/SOV Upgrades/Pyerite Prospecting Array 2.png" },
            { "Pyerite Prospecting Array 3", "Images/SOV Upgrades/Pyerite Prospecting Array 3.png" },

            { "Tritanium Prospecting Array 1", "Images/SOV Upgrades/Tritanium Prospecting Array 1.png" },
            { "Tritanium Prospecting Array 2", "Images/SOV Upgrades/Tritanium Prospecting Array 2.png" },
            { "Tritanium Prospecting Array 3", "Images/SOV Upgrades/Tritanium Prospecting Array 3.png" },

            { "Zydrine Prospecting Array 1", "Images/SOV Upgrades/Zydrine Prospecting Array 1.png" },
            { "Zydrine Prospecting Array 2", "Images/SOV Upgrades/Zydrine Prospecting Array 2.png" },
            { "Zydrine Prospecting Array 3", "Images/SOV Upgrades/Zydrine Prospecting Array 3.png" }
        };

        public static IReadOnlyCollection<string> DisplayNames => IconPaths.Keys;

        public static bool TryGetIconPath(string displayName, out string iconPath)
        {
            return IconPaths.TryGetValue(displayName, out iconPath);
        }
    }
}


