// LaneShiftSettings.cs
// Uses ColossalFramework SavedInt/SavedBool — the standard CS1 settings pattern.
// Values are persisted automatically; no explicit Save() call needed.
using ColossalFramework;
using UnityEngine;

namespace LaneShifter
{
    public static class LaneShiftSettings
    {
        private const string FILE_NAME = "LaneShifter";

        // Hotkey
        public static readonly SavedInt  HotkeyCode  = new SavedInt(  "hotkeyCode",  FILE_NAME, (int)KeyCode.None, true);
        public static readonly SavedBool HotkeyShift = new SavedBool( "hotkeyShift", FILE_NAME, false,             true);
        public static readonly SavedBool HotkeyCtrl  = new SavedBool( "hotkeyCtrl",  FILE_NAME, false,             true);
        public static readonly SavedBool HotkeyAlt   = new SavedBool( "hotkeyAlt",   FILE_NAME, false,             true);

        // UI visibility
        public static readonly SavedBool ShowInUUI            = new SavedBool("showInUUI",            FILE_NAME, true, true);
        public static readonly SavedBool ShowStandaloneButton = new SavedBool("showStandaloneButton", FILE_NAME, true, true);

        // Helper: check if the configured hotkey is currently pressed
        public static bool IsHotkeyPressed()
        {
            if (HotkeyCode.value == (int)KeyCode.None) return false;
            if (!Input.GetKeyDown((KeyCode)HotkeyCode.value)) return false;

            bool shift = Input.GetKey(KeyCode.LeftShift)   || Input.GetKey(KeyCode.RightShift);
            bool ctrl  = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt   = Input.GetKey(KeyCode.LeftAlt)     || Input.GetKey(KeyCode.RightAlt);

            if (HotkeyShift.value != shift) return false;
            if (HotkeyCtrl.value  != ctrl)  return false;
            if (HotkeyAlt.value   != alt)   return false;
            return true;
        }

        // Helper: human-readable key binding string for UI display
        public static string HotkeyDisplay()
        {
            if (HotkeyCode.value == (int)KeyCode.None) return "(unbound)";
            string s = "";
            if (HotkeyCtrl.value)  s += "Ctrl+";
            if (HotkeyShift.value) s += "Shift+";
            if (HotkeyAlt.value)   s += "Alt+";
            s += ((KeyCode)HotkeyCode.value).ToString();
            return s;
        }
    }
}
