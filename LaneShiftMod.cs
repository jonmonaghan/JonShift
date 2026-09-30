// LaneShiftMod.cs — contains all settings, hotkey capture, and loading logic.
// No new .cs files needed — just replace this and LaneShiftButton.cs.
using System;
using System.IO;
using System.Reflection;
using ColossalFramework.UI;
using HarmonyLib;
using ICities;
using UnityEngine;

namespace LaneShifter
{
    // ---------------------------------------------------------------
    // Settings (plain static fields + text file, no serialization)
    // ---------------------------------------------------------------
    internal static class LaneShiftSettings
    {
        public static int  HotkeyCode  = (int)KeyCode.None;
        public static bool HotkeyShift = false;
        public static bool HotkeyCtrl  = false;
        public static bool HotkeyAlt   = false;
        public static bool ShowInUUI            = true;
        public static bool ShowStandaloneButton  = true;

        private static string SettingsPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Colossal Order", "Cities_Skylines", "LaneShifterSettings.txt");

        public static void Save()
        {
            try
            {
                File.WriteAllLines(SettingsPath, new string[]
                {
                    "HotkeyCode="            + HotkeyCode.ToString(),
                    "HotkeyShift="           + HotkeyShift.ToString(),
                    "HotkeyCtrl="            + HotkeyCtrl.ToString(),
                    "HotkeyAlt="             + HotkeyAlt.ToString(),
                    "ShowInUUI="             + ShowInUUI.ToString(),
                    "ShowStandaloneButton="  + ShowStandaloneButton.ToString(),
                });
            }
            catch (Exception ex) { Debug.LogWarning("[LaneShifter] Settings save: " + ex.Message); }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                string[] lines = File.ReadAllLines(SettingsPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    int eq = lines[i].IndexOf('=');
                    if (eq < 0) continue;
                    string k = lines[i].Substring(0, eq).Trim();
                    string v = lines[i].Substring(eq + 1).Trim();
                    int iv; bool bv;
                    if      (k == "HotkeyCode"           && int.TryParse(v,  out iv)) HotkeyCode  = iv;
                    else if (k == "HotkeyShift"          && bool.TryParse(v, out bv)) HotkeyShift = bv;
                    else if (k == "HotkeyCtrl"           && bool.TryParse(v, out bv)) HotkeyCtrl  = bv;
                    else if (k == "HotkeyAlt"            && bool.TryParse(v, out bv)) HotkeyAlt   = bv;
                    else if (k == "ShowInUUI"            && bool.TryParse(v, out bv)) ShowInUUI   = bv;
                    else if (k == "ShowStandaloneButton" && bool.TryParse(v, out bv)) ShowStandaloneButton = bv;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[LaneShifter] Settings load: " + ex.Message); }
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

    // ---------------------------------------------------------------
    // Key capture (waits one frame then reads next non-modifier key)
    // ---------------------------------------------------------------
    internal sealed class LaneShiftKeyCapture : MonoBehaviour
    {
        // Fixed key list — avoids Enum.GetValues which can crash on old Mono
        private static readonly KeyCode[] Keys = new KeyCode[]
        {
            KeyCode.A,KeyCode.B,KeyCode.C,KeyCode.D,KeyCode.E,KeyCode.F,
            KeyCode.G,KeyCode.H,KeyCode.I,KeyCode.J,KeyCode.K,KeyCode.L,
            KeyCode.M,KeyCode.N,KeyCode.O,KeyCode.P,KeyCode.Q,KeyCode.R,
            KeyCode.S,KeyCode.T,KeyCode.U,KeyCode.V,KeyCode.W,KeyCode.X,
            KeyCode.Y,KeyCode.Z,
            KeyCode.Alpha0,KeyCode.Alpha1,KeyCode.Alpha2,KeyCode.Alpha3,
            KeyCode.Alpha4,KeyCode.Alpha5,KeyCode.Alpha6,KeyCode.Alpha7,
            KeyCode.Alpha8,KeyCode.Alpha9,
            KeyCode.F1,KeyCode.F2,KeyCode.F3,KeyCode.F4,KeyCode.F5,KeyCode.F6,
            KeyCode.F7,KeyCode.F8,KeyCode.F9,KeyCode.F10,KeyCode.F11,KeyCode.F12,
            KeyCode.Tab,KeyCode.Space,KeyCode.BackQuote,KeyCode.Minus,KeyCode.Equals,
            KeyCode.LeftBracket,KeyCode.RightBracket,KeyCode.Backslash,
            KeyCode.Semicolon,KeyCode.Quote,KeyCode.Comma,KeyCode.Period,KeyCode.Slash,
        };

        private Action<KeyCode, bool, bool, bool> _cb;
        private bool _armed;

        public static void Begin(Action<KeyCode, bool, bool, bool> callback)
        {
            var go = new GameObject("LaneShifterKeyCapture");
            var kc = go.AddComponent<LaneShiftKeyCapture>();
            kc._cb    = callback;
            kc._armed = false;
            DontDestroyOnLoad(go);
        }

        void Update()
        {
            if (!_armed) { _armed = true; return; }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _cb(KeyCode.None, false, false, false);
                Destroy(gameObject);
                return;
            }
            if (!Input.anyKeyDown) return;
            bool shift = Input.GetKey(KeyCode.LeftShift)   || Input.GetKey(KeyCode.RightShift);
            bool ctrl  = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt   = Input.GetKey(KeyCode.LeftAlt)     || Input.GetKey(KeyCode.RightAlt);
            for (int i = 0; i < Keys.Length; i++)
            {
                if (Input.GetKeyDown(Keys[i]))
                {
                    _cb(Keys[i], shift, ctrl, alt);
                    Destroy(gameObject);
                    return;
                }
            }
        }
    }

    // ---------------------------------------------------------------
    // Mod entry point + options UI
    // ---------------------------------------------------------------
    public class LaneShiftMod : IUserMod
    {
        public string Name        => "Lane Shifter";
        public string Description => "Shift individual road lanes laterally.";

        private static UIButton _rebindBtn;

        public void OnSettingsUI(UIHelperBase helper)
        {
            LaneShiftSettings.Load();

            UIHelperBase hotkeyGroup = helper.AddGroup("Hotkey");
            _rebindBtn = hotkeyGroup.AddButton(
                "Current: " + LaneShiftSettings.HotkeyDisplay(),
                OnRebind) as UIButton;
            if (!object.ReferenceEquals(_rebindBtn, null))
                _rebindBtn.tooltip = "Click, then press any key. Esc = clear.";

            UIHelperBase visGroup = helper.AddGroup("Button Visibility");
            visGroup.AddCheckbox("Show in UnifiedUI toolbar (next load)",
                LaneShiftSettings.ShowInUUI,
                v => { LaneShiftSettings.ShowInUUI = v; LaneShiftSettings.Save(); });
            visGroup.AddCheckbox("Show standalone screen button",
                LaneShiftSettings.ShowStandaloneButton,
                v =>
                {
                    LaneShiftSettings.ShowStandaloneButton = v;
                    LaneShiftSettings.Save();
                    if (!object.ReferenceEquals(LaneShiftButton.Instance, null))
                        LaneShiftButton.Instance.isVisible = v;
                });
        }

        private static void OnRebind()
        {
            if (!object.ReferenceEquals(_rebindBtn, null))
                _rebindBtn.text = "Press a key... (Esc = clear)";
            LaneShiftKeyCapture.Begin((key, shift, ctrl, alt) =>
            {
                LaneShiftSettings.HotkeyCode  = (int)key;
                LaneShiftSettings.HotkeyShift = shift;
                LaneShiftSettings.HotkeyCtrl  = ctrl;
                LaneShiftSettings.HotkeyAlt   = alt;
                LaneShiftSettings.Save();
                if (!object.ReferenceEquals(_rebindBtn, null))
                    _rebindBtn.text = "Current: " + LaneShiftSettings.HotkeyDisplay();
            });
        }
    }

    // ---------------------------------------------------------------
    // Hotkey monitor
    // ---------------------------------------------------------------
    public class LaneShiftHotkeyMonitor : MonoBehaviour
    {
        void Update()
        {
            if (!LaneShiftSettings.IsHotkeyPressed()) return;
            if (object.ReferenceEquals(ToolsModifierControl.toolController, null)) return;
            if (object.ReferenceEquals(LaneShiftTool.Instance, null)) return;
            if (object.ReferenceEquals(ToolsModifierControl.toolController.CurrentTool, LaneShiftTool.Instance))
                LaneShiftTool.DisableTool();
            else
                LaneShiftTool.EnableTool();
        }
    }

    // ---------------------------------------------------------------
    // Loading
    // ---------------------------------------------------------------
    public class LaneShiftLoading : LoadingExtensionBase
    {
        private const string HARMONY_ID = "com.jonmonaghan.laneshifter";
        private Harmony    _harmony;
        private GameObject _hotkeyObj;

        public override void OnCreated(ILoading loading)
        {
            LaneShiftSettings.Load();
            _harmony = new Harmony(HARMONY_ID);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        public override void OnLevelLoaded(LoadMode mode)
        {
            if (mode != LoadMode.LoadGame && mode != LoadMode.NewGame) return;
            LaneShiftManager.Create();
            if (!object.ReferenceEquals(LaneShiftManager.PendingLoadData, null))
            {
                LaneShiftManager.Instance.Deserialize(LaneShiftManager.PendingLoadData);
                LaneShiftManager.PendingLoadData = null;
            }
            LaneShiftTool.Create();
            LaneShiftPanel.Create();
            _hotkeyObj = new GameObject("LaneShifterHotkey");
            UnityEngine.Object.DontDestroyOnLoad(_hotkeyObj);
            _hotkeyObj.AddComponent<LaneShiftHotkeyMonitor>();
            UIView.GetAView().AddUIComponent(typeof(LaneShiftButton));
            if (LaneShiftSettings.ShowInUUI) TryRegisterWithUUI();
        }

        public override void OnLevelUnloading()
        {
            LaneShiftPanel.Destroy();
            if (!object.ReferenceEquals(LaneShiftButton.Instance, null))
                UnityEngine.Object.Destroy(LaneShiftButton.Instance.gameObject);
            if (!object.ReferenceEquals(_hotkeyObj, null))
            {
                UnityEngine.Object.Destroy(_hotkeyObj);
                _hotkeyObj = null;
            }
            LaneShiftTool.Remove();
            LaneShiftManager.Release();
        }

        public override void OnReleased() => _harmony?.UnpatchAll(HARMONY_ID);

        private static void TryRegisterWithUUI()
        {
            try
            {
                Type helpers = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    if (string.Equals(asm.GetName().Name, "UnifiedUILib", StringComparison.Ordinal))
                    { helpers = asm.GetType("UnifiedUI.Helpers.UUIHelpers"); break; }
                if (object.ReferenceEquals(helpers, null)) return;

                MethodInfo bestTex = null, bestOther = null;
                foreach (MethodInfo m in helpers.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!string.Equals(m.Name, "RegisterToolButton", StringComparison.Ordinal)) continue;
                    bool hasTool = false, hasTexArr = false;
                    foreach (ParameterInfo p in m.GetParameters())
                    {
                        if (typeof(ToolBase).IsAssignableFrom(p.ParameterType)) hasTool = true;
                        if (string.Equals(p.ParameterType.FullName, "UnityEngine.Texture2D[]", StringComparison.Ordinal)) hasTexArr = true;
                    }
                    if (!hasTool) continue;
                    if (hasTexArr) bestTex = m; else bestOther = m;
                }
                MethodInfo reg = !object.ReferenceEquals(bestTex, null) ? bestTex : bestOther;
                if (object.ReferenceEquals(reg, null)) return;

                ParameterInfo[] ps  = reg.GetParameters();
                object[]        args = new object[ps.Length];
                Texture2D       ico  = LoadIcon();
                Texture2D[]     icos = new Texture2D[] { ico };
                int si = 0;
                for (int i = 0; i < ps.Length; i++)
                {
                    string fn = ps[i].ParameterType.FullName;
                    if (string.Equals(fn, "System.String", StringComparison.Ordinal))
                    { args[i] = si == 0 ? "LaneShifter" : si == 1 ? null : (object)"Lane Shifter"; si++; }
                    else if (typeof(ToolBase).IsAssignableFrom(ps[i].ParameterType)) args[i] = LaneShiftTool.Instance;
                    else if (string.Equals(fn, "UnityEngine.Texture2D[]", StringComparison.Ordinal)) args[i] = icos;
                    else if (string.Equals(fn, "UnityEngine.Texture2D",   StringComparison.Ordinal)) args[i] = ico;
                    else args[i] = null;
                }
                reg.Invoke(null, args);
            }
            catch (Exception ex) { Debug.LogWarning("[LaneShifter] UUI: " + ex.Message); }
        }

        private static Texture2D LoadIcon()
        {
            try
            {
                string folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string path   = Path.Combine(folder, "icon.png");
                if (File.Exists(path))
                {
                    byte[] buf = File.ReadAllBytes(path);
                    Texture2D t = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                    if (t.LoadImage(buf)) { t.Apply(); return t; }
                }
            }
            catch { }
            Texture2D fb = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            Color32 c = new Color32(80, 200, 120, 255);
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) fb.SetPixel(x, y, c);
            fb.Apply();
            return fb;
        }
    }
}
