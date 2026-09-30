using System;
using System.IO;
using UnityEngine;

namespace LaneShifter
{
    public static class LaneShiftSettings
    {
        public static int  HotkeyCode  = (int)KeyCode.None;
        public static bool HotkeyShift = false;
        public static bool HotkeyCtrl  = false;
        public static bool HotkeyAlt   = false;
        public static bool ShowInUUI             = true;
        public static bool ShowStandaloneButton  = true;

        private static string SettingsPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Colossal Order", "Cities_Skylines", "LaneShifterSettings.txt");

        public static void Save()
        {
            try
            {
                string[] lines = new string[]
                {
                    "HotkeyCode="  + HotkeyCode.ToString(),
                    "HotkeyShift=" + HotkeyShift.ToString(),
                    "HotkeyCtrl="  + HotkeyCtrl.ToString(),
                    "HotkeyAlt="   + HotkeyAlt.ToString(),
                    "ShowInUUI="             + ShowInUUI.ToString(),
                    "ShowStandaloneButton="  + ShowStandaloneButton.ToString(),
                };
                File.WriteAllLines(SettingsPath, lines);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LaneShifter] Settings save: " + ex.Message);
            }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                string[] lines = File.ReadAllLines(SettingsPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    int  iv; bool bv;
                    if      (k == "HotkeyCode"  && int.TryParse(v,  out iv)) HotkeyCode  = iv;
                    else if (k == "HotkeyShift" && bool.TryParse(v, out bv)) HotkeyShift = bv;
                    else if (k == "HotkeyCtrl"  && bool.TryParse(v, out bv)) HotkeyCtrl  = bv;
                    else if (k == "HotkeyAlt"   && bool.TryParse(v, out bv)) HotkeyAlt   = bv;
                    else if (k == "ShowInUUI"   && bool.TryParse(v, out bv)) ShowInUUI   = bv;
                    else if (k == "ShowStandaloneButton" && bool.TryParse(v, out bv)) ShowStandaloneButton = bv;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LaneShifter] Settings load: " + ex.Message);
            }
        }

        public static bool IsHotkeyPressed()
        {
            if (HotkeyCode == (int)KeyCode.None) return false;
            if (!Input.GetKeyDown((KeyCode)HotkeyCode)) return false;
            bool shift = Input.GetKey(KeyCode.LeftShift)   || Input.GetKey(KeyCode.RightShift);
            bool ctrl  = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt   = Input.GetKey(KeyCode.LeftAlt)     || Input.GetKey(KeyCode.RightAlt);
            return HotkeyShift == shift && HotkeyCtrl == ctrl && HotkeyAlt == alt;
        }

        public static string HotkeyDisplay()
        {
            if (HotkeyCode == (int)KeyCode.None) return "(unbound)";
            string s = "";
            if (HotkeyCtrl)  s += "Ctrl+";
            if (HotkeyShift) s += "Shift+";
            if (HotkeyAlt)   s += "Alt+";
            return s + ((KeyCode)HotkeyCode).ToString();
        }
    }
}
