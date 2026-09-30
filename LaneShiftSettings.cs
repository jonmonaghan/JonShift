using System;
using System.IO;
using System.Xml.Serialization;
using UnityEngine;

namespace LaneShifter
{
    [XmlRoot("LaneShifterSettings")]
    public class LaneShiftSettings
    {
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
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Colossal Order", "Cities_Skylines", "LaneShifterSettings.xml");

        // Stored as int (KeyCode enum value). 0 = None = unbound.
        [XmlElement] public int  HotkeyCode  = (int)KeyCode.None;
        [XmlElement] public bool HotkeyShift = false;
        [XmlElement] public bool HotkeyCtrl  = false;
        [XmlElement] public bool HotkeyAlt   = false;

        [XmlElement] public bool ShowInUUI           = true;
        [XmlElement] public bool ShowStandaloneButton = true;

        public bool IsHotkeyPressed()
        {
            if (HotkeyCode == (int)KeyCode.None) return false;
            if (!Input.GetKeyDown((KeyCode)HotkeyCode)) return false;
            bool shift = Input.GetKey(KeyCode.LeftShift)  || Input.GetKey(KeyCode.RightShift);
            bool ctrl  = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt   = Input.GetKey(KeyCode.LeftAlt)    || Input.GetKey(KeyCode.RightAlt);
            if (HotkeyShift != shift) return false;
            if (HotkeyCtrl  != ctrl)  return false;
            if (HotkeyAlt   != alt)   return false;
            return true;
        }

        public string HotkeyDisplay()
        {
            if (HotkeyCode == (int)KeyCode.None) return "(unbound)";
            string s = "";
            if (HotkeyCtrl)  s += "Ctrl+";
            if (HotkeyShift) s += "Shift+";
            if (HotkeyAlt)   s += "Alt+";
            s += ((KeyCode)HotkeyCode).ToString();
            return s;
        }

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
            catch (Exception ex) { Debug.LogWarning("[LaneShifter] Settings load: " + ex.Message); }
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
            catch (Exception ex) { Debug.LogWarning("[LaneShifter] Settings save: " + ex.Message); }
        }
    }
}
