//-----------------------------------------------------------------------
// Region Layout Overrides
//-----------------------------------------------------------------------

using System.Numerics;
using EVEDataUtils;

namespace HISA.EVEData
{
    public class RegionLayoutOverrides
    {
        public string RegionName { get; set; }

        public SerializableDictionary<string, Vector2> Positions { get; set; } = new SerializableDictionary<string, Vector2>();
    }
}


