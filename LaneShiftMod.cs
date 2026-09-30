// LaneShiftMod.cs — fix9
// OnSettingsUI uses only UIHelperBase methods (no UIHelper cast, no raw UIComponent access).
using System;
using System.Reflection;
using ColossalFramework.UI;
using HarmonyLib;
using ICities;
using UnityEngine;

namespace LaneShifter
{
    public class LaneShiftMod : IUserMod
    {
        public string Name        => "Lane Shifter";
        public string Description => "Shift individual road lanes laterally.";

        // Kept as a field so the keycapture callback can update the button label.
        private static UIButton _rebindBtn;

        public void OnSettingsUI(UIHelperBase helper)
        {
            LaneShiftSettings.Load();

            // ---- Hotkey ----
            UIHelperBase hotkeyGroup = helper.AddGroup("Hotkey");

            // Rebind button — AddButton returns object; cast to UIButton is safe here.
            _rebindBtn = hotkeyGroup.AddButton(
                "Current: " + LaneShiftSettings.HotkeyDisplay(),
                OnRebindClicked) as UIButton;

            if (!object.ReferenceEquals(_rebindBtn, null))
            {
                _rebindBtn.tooltip = "Click, then press any key combination. Esc = clear binding.";
            }

            // ---- Button Visibility ----
            UIHelperBase visGroup = helper.AddGroup("Button Visibility");

            visGroup.AddCheckbox(
                "Show in UnifiedUI toolbar (takes effect on next level load)",
                LaneShiftSettings.ShowInUUI,
                v => { LaneShiftSettings.ShowInUUI = v; LaneShiftSettings.Save(); });

            visGroup.AddCheckbox(
                "Show standalone button on screen",
                LaneShiftSettings.ShowStandaloneButton,
                v =>
                {
                    LaneShiftSettings.ShowStandaloneButton = v;
                    LaneShiftSettings.Save();
                    if (!object.ReferenceEquals(LaneShiftButton.Instance, null))
                        LaneShiftButton.Instance.isVisible = v;
                });
        }

        private static void OnRebindClicked()
        {
            if (!object.ReferenceEquals(_rebindBtn, null))
                _rebindBtn.text = "Press a key... (Esc = clear)";

            KeyCapture.Start((key, shift, ctrl, alt) =>
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
    public class LaneShiftHotkeyMonitor : UnityEngine.MonoBehaviour
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

            // Standalone button (non-generic UIView pattern — confirmed working)
            UIView.GetAView().AddUIComponent(typeof(LaneShiftButton));

            if (LaneShiftSettings.ShowInUUI)
                TryRegisterWithUUI();
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

        // ---- UUI ----
        private static void TryRegisterWithUUI()
        {
            try
            {
                Type helpers = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Equals(asm.GetName().Name, "UnifiedUILib", StringComparison.Ordinal))
                    {
                        helpers = asm.GetType("UnifiedUI.Helpers.UUIHelpers");
                        break;
                    }
                }
                if (object.ReferenceEquals(helpers, null)) { Debug.Log("[LaneShifter] UUI not found."); return; }

                // Prefer the Texture2D[] overload over the string-iconpath overload
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
                    if (hasTexArr) bestTex   = m;
                    else           bestOther  = m;
                }

                MethodInfo register = !object.ReferenceEquals(bestTex, null) ? bestTex : bestOther;
                if (object.ReferenceEquals(register, null)) { Debug.LogWarning("[LaneShifter] UUI: no overload found."); return; }

                ParameterInfo[] ps     = register.GetParameters();
                object[]        args   = new object[ps.Length];
                Texture2D       icon   = LoadIcon();
                Texture2D[]     icons  = new Texture2D[] { icon };
                int strIdx = 0;

                for (int i = 0; i < ps.Length; i++)
                {
                    string fn = ps[i].ParameterType.FullName;
                    if (string.Equals(fn, "System.String", StringComparison.Ordinal))
                    {
                        if      (strIdx == 0) args[i] = "LaneShifter";
                        else if (strIdx == 1) args[i] = null;
                        else                  args[i] = "Lane Shifter";
                        strIdx++;
                    }
                    else if (typeof(ToolBase).IsAssignableFrom(ps[i].ParameterType)) args[i] = LaneShiftTool.Instance;
                    else if (string.Equals(fn, "UnityEngine.Texture2D[]", StringComparison.Ordinal)) args[i] = icons;
                    else if (string.Equals(fn, "UnityEngine.Texture2D",   StringComparison.Ordinal)) args[i] = icon;
                    else args[i] = null;
                }

                object result = register.Invoke(null, args);
                Debug.Log("[LaneShifter] UUI registered: " + result);
            }
            catch (Exception ex) { Debug.LogWarning("[LaneShifter] UUI error: " + ex); }
        }

        // ---- Icon loader ----
        private static Texture2D LoadIcon()
        {
            try
            {
                string folder   = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string iconPath = System.IO.Path.Combine(folder, "icon.png");
                if (System.IO.File.Exists(iconPath))
                {
                    byte[]    buf = System.IO.File.ReadAllBytes(iconPath);
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                    if (tex.LoadImage(buf)) { tex.Apply(); return tex; }
                }
            }
            catch (Exception ex) { Debug.LogWarning("[LaneShifter] Icon: " + ex.Message); }

            // Fallback green square
            Texture2D fb = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            Color32 fill = new Color32(80, 200, 120, 255);
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) fb.SetPixel(x, y, fill);
            fb.Apply();
            return fb;
        }
    }
}
