using System;
using System.Collections.Generic;
using System.Linq;
using NHotkey;
using NHotkey.Wpf;
using HISA.EVEData;

namespace HISA.Helpers;

public static class OverlayWindowManager
{
    public const string ToggleClickThroughHotkeyName = "Toggle click trough overlay windows.";

    public static bool CanOpenOverlay(MapConfig mapConfig, LocalCharacter activeCharacter, IEnumerable<Overlay> overlays)
    {
        if(mapConfig == null)
        {
            return false;
        }

        List<Overlay> openOverlays = overlays?.ToList() ?? new List<Overlay>();

        if(mapConfig.OverlayIndividualCharacterWindows)
        {
            return activeCharacter != null && !openOverlays.Any(w => w.OverlayCharacter == activeCharacter);
        }

        return openOverlays.Count == 0;
    }

    public static void EnsureToggleHotkeyRegistered(EventHandler<HotkeyEventArgs> hotkeyHandler)
    {
        try
        {
            HotkeyManager.Current.AddOrReplace(
                ToggleClickThroughHotkeyName,
                System.Windows.Input.Key.T,
                System.Windows.Input.ModifierKeys.Alt | System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift,
                hotkeyHandler);
        }
        catch(HotkeyAlreadyRegisteredException)
        {
        }
    }

    public static bool ToggleClickThrough(IList<Overlay> overlays, bool currentState)
    {
        bool nextState = !currentState;
        if(overlays != null)
        {
            foreach(Overlay overlayWindow in overlays)
            {
                overlayWindow.ToggleClickTrough(nextState);
            }
        }

        return nextState;
    }

    public static void HandleOverlayClosing(IList<Overlay> overlays, Overlay closingOverlay)
    {
        if(overlays == null || closingOverlay == null)
        {
            return;
        }

        overlays.Remove(closingOverlay);

        if(overlays.Count < 1)
        {
            try
            {
                HotkeyManager.Current.Remove(ToggleClickThroughHotkeyName);
            }
            catch
            {
            }
        }
    }
}
