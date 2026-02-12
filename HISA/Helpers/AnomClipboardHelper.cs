using System.Collections.Generic;
using System.Linq;
using HISA.EVEData;

namespace HISA.Helpers;

public static class AnomClipboardHelper
{
    public static string BuildClipboardText(IEnumerable<Anom> selectedAnoms, IEnumerable<Anom> allAnoms)
    {
        List<Anom> selected = selectedAnoms?.Where(a => a != null).ToList() ?? new List<Anom>();
        IEnumerable<Anom> source = selected.Count > 0
            ? selected
            : (allAnoms ?? Enumerable.Empty<Anom>()).Where(a => a != null);

        List<string> lines = source.Select(anom => anom.ToString()).ToList();
        if(lines.Count == 0)
        {
            return string.Empty;
        }

        return string.Join("\n", lines) + "\n";
    }

    public static HashSet<string> GetSelectedSignatures(IEnumerable<Anom> selectedAnoms)
    {
        return selectedAnoms?
            .Where(anom => anom != null && !string.IsNullOrWhiteSpace(anom.Signature))
            .Select(anom => anom.Signature)
            .ToHashSet()
            ?? new HashSet<string>();
    }
}
