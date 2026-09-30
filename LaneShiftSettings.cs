using System;
using System.IO;
using System.Xml.Serialization;
using ColossalFramework;
using UnityEngine;

namespace LaneShifter
{
    [XmlRoot("LaneShifterSettings")]
    public class LaneShiftSettings
    {
        // ---- Singleton ----
        private static LaneShiftSettings _instance;
        public static LaneShiftSettings Instance
        {
            get
            {
                if (object.ReferenceEquals(_instance, null))
                    _instance = Load();
                return _instance;
            }
        }

        private static string SettingsPath =>
            Path.Combine(DataLocation.localApplicationData, "LaneShifterSettings.xml");

        // ---- Settings ----
        // Hotkey: stored as KeyCode int. 0 = None (unbound).
        [XmlElement] public int  HotkeyCode     = (int)KeyCode.None;
        [XmlElement] public bool HotkeyShift    = false;
        [XmlElement] public bool HotkeyCtrl     = false;
        [XmlElement] public bool HotkeyAlt      = false;
        [XmlElement] public bool ShowInUUI       = true;
        [XmlElement] public bool ShowStandaloneButton = true;

        // ---- Helpers ----
        public bool IsHotkeyPressed()
        {
            if (HotkeyCode == (int)KeyCode.None) return false;
            var key = (KeyCode)HotkeyCode;
            if (!Input.GetKeyDown(key)) return false;
            if (HotkeyShift && !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift)) return false;
            if (HotkeyCtrl  && !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl)) return false;
            if (HotkeyAlt   && !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt)) return false;
            return true;
        }

        public string HotkeyDisplayString()
        {
            if (HotkeyCode == (int)KeyCode.None) return "(unbound)";
            string s = "";
            if (HotkeyCtrl)  s += "Ctrl+";
            if (HotkeyShift) s += "Shift+";
            if (HotkeyAlt)   s += "Alt+";
            s += ((KeyCode)HotkeyCode).ToString();
            return s;
        }

        // ---- Persistence ----
        public static LaneShiftSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var xs = new XmlSerializer(typeof(LaneShiftSettings));
                    using (var sr = new StreamReader(SettingsPath))
                        return (LaneShiftSettings)xs.Deserialize(sr);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LaneShifter] Settings load error: " + ex.Message);
            }
            return new LaneShiftSettings();
        }

        public void Save()
        {
            try
            {
                var xs = new XmlSerializer(typeof(LaneShiftSettings));
                using (var sw = new StreamWriter(SettingsPath))
                    xs.Serialize(sw, this);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LaneShifter] Settings save error: " + ex.Message);
            }
        }
    }
}
