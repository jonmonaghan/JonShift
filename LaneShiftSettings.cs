// LaneShiftSettings.cs
// Plain key=value text file. No XmlSerializer, no ColossalFramework types.
// Works on any Mono/.NET version CS1 ships with.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LaneShifter
{
    public static class LaneShiftSettings
    {
        // Hotkey (default = unbound)
        public static int  HotkeyCode  = (int)KeyCode.None;
        public static bool HotkeyShift = false;
        public static bool HotkeyCtrl  = false;
        public static bool HotkeyAlt   = false;

        // UI
        public static bool ShowInUUI            = true;
        public static bool ShowStandaloneButton  = true;

        private static string SettingsPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Colossal Order", "Cities_Skylines", "LaneShifterSettings.txt");

        // ---- Persistence ----
        public static void Save()
        {
            try
            {
                string[] lines =
                {
                    "HotkeyCode="  + HotkeyCode,
                    "HotkeyShift=" + HotkeyShift,
                    "HotkeyCtrl="  + HotkeyCtrl,
                    "HotkeyAlt="   + HotkeyAlt,
                    "ShowInUUI="            + ShowInUUI,
                    "ShowStandaloneButton=" + ShowStandaloneButton,
                };
                File.WriteAllLines(SettingsPath, lines);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LaneShifter] Settings save error: " + ex.Message);
            }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                foreach (string rawLine in File.ReadAllLines(SettingsPath))
                {
                    string line = rawLine.Trim();
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "HotkeyCode":           int.TryParse(val,  out HotkeyCode);           break;
                        case "HotkeyShift":          bool.TryParse(val, out HotkeyShift);          break;
                        case "HotkeyCtrl":           bool.TryParse(val, out HotkeyCtrl);           break;
                        case "HotkeyAlt":            bool.TryParse(val, out HotkeyAlt);            break;
                        case "ShowInUUI":            bool.TryParse(val, out ShowInUUI);            break;
                        case "ShowStandaloneButton": bool.TryParse(val, out ShowStandaloneButton); break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LaneShifter] Settings load error: " + ex.Message);
            }
        }

        // ---- Helpers ----
        public static bool IsHotkeyPressed()
        {
            if (HotkeyCode == (int)KeyCode.None) return false;
            if (!Input.GetKeyDown((KeyCode)HotkeyCode)) return false;
            bool shift = Input.GetKey(KeyCode.LeftShift)   || Input.GetKey(KeyCode.RightShift);
            bool ctrl  = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt   = Input.GetKey(KeyCode.LeftAlt)     || Input.GetKey(KeyCode.RightAlt);
            if (HotkeyShift != shift) return false;
            if (HotkeyCtrl  != ctrl)  return false;
            if (HotkeyAlt   != alt)   return false;
            return true;
        }

        public static string HotkeyDisplay()
        {
            if (HotkeyCode == (int)KeyCode.None) return "(unbound)";
            string s = "";
            if (HotkeyCtrl)  s += "Ctrl+";
            if (HotkeyShift) s += "Shift+";
            if (HotkeyAlt)   s += "Alt+";
            s += ((KeyCode)HotkeyCode).ToString();
            return s;
        }
    }
}
