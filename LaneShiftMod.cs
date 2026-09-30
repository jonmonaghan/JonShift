using System;
using System.IO;
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

        public void OnSettingsUI(UIHelperBase helper)
        {
            var cfg = LaneShiftSettings.Instance;

            // ---- Hotkey ----
            UIHelperBase hotkeyGroup = helper.AddGroup("Hotkey");

            // The rebind button shows current binding; click to capture next keypress
            string currentLabel = "Current: " + cfg.HotkeyDisplay();
            UIButton[] rebindRef = new UIButton[1]; // ref trick for closure

            // We need the underlying panel to add a raw UIButton
            UIScrollablePanel rootPanel = ((UIHelper)helper).self as UIScrollablePanel;

            // Add a standard label via helper
            hotkeyGroup.AddSpace(4);

            // Get the group panel to add our custom button to
            UIScrollablePanel groupPanel = ((UIHelper)hotkeyGroup).self as UIScrollablePanel;

            if (!object.ReferenceEquals(groupPanel, null))
            {
                UIButton rebind = groupPanel.AddUIComponent<UIButton>();
                rebindRef[0] = rebind;

                rebind.text           = currentLabel;
                rebind.width          = 250f;
                rebind.height         = 30f;
                rebind.textScale      = 0.85f;
                rebind.textPadding    = new RectOffset(6, 6, 4, 4);
                rebind.normalBgSprite = "ButtonMenu";
                rebind.hoveredBgSprite= "ButtonMenuHovered";
                rebind.pressedBgSprite= "ButtonMenuPressed";
                rebind.textColor      = Color.white;
                rebind.playAudioEvents= true;
                rebind.tooltip        = "Click then press any key. Esc = clear binding.";

                rebind.eventClicked += (c, p) =>
                {
                    rebind.text = "Press a key... (Esc to clear)";
                    KeyCapture.Start((key, shift, ctrl, alt) =>
                    {
                        cfg.HotkeyCode  = (int)key;
                        cfg.HotkeyShift = shift;
                        cfg.HotkeyCtrl  = ctrl;
                        cfg.HotkeyAlt   = alt;
                        cfg.Save();
                        rebind.text = "Current: " + cfg.HotkeyDisplay();
                    });
                };
            }
            else
            {
                // Fallback: can't get panel, show text-only instructions
                hotkeyGroup.AddSpace(4);
            }

            // ---- Button visibility ----
            UIHelperBase visGroup = helper.AddGroup("Button Visibility");

            visGroup.AddCheckbox("Show in UnifiedUI toolbar (takes effect on next level load)",
                cfg.ShowInUUI, v => { cfg.ShowInUUI = v; cfg.Save(); });

            visGroup.AddCheckbox("Show standalone button on screen",
                cfg.ShowStandaloneButton, v =>
                {
                    cfg.ShowStandaloneButton = v;
                    cfg.Save();
                    if (!object.ReferenceEquals(LaneShiftButton.Instance, null))
                        LaneShiftButton.Instance.isVisible = v;
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
            if (!LaneShiftSettings.Instance.IsHotkeyPressed()) return;
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

            if (LaneShiftSettings.Instance.ShowInUUI)
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

        // ---- Shared icon loader (used by both UUI and standalone button) ----
        public static Texture2D LoadIcon()
        {
            try
            {
                string folder   = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string iconPath = Path.Combine(folder, "icon.png");
                if (File.Exists(iconPath))
                {
                    byte[]    buf = File.ReadAllBytes(iconPath);
                    // Start with 2x2; LoadImage auto-resizes to actual PNG dimensions
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                    if (tex.LoadImage(buf))
                    {
                        tex.Apply();
                        Debug.Log("[LaneShifter] Icon loaded " + tex.width + "x" + tex.height);
                        return tex;
                    }
                    Debug.LogWarning("[LaneShifter] LoadImage returned false for " + iconPath);
                }
                else
                {
                    Debug.LogWarning("[LaneShifter] icon.png not found at " + folder);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LaneShifter] Icon error: " + ex.Message);
            }
            return null; // callers handle null = use fallback
        }

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

                // Prefer overload that has Texture2D[] (not string icon path)
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
                if (object.ReferenceEquals(register, null)) { Debug.LogWarning("[LaneShifter] UUI: method not found."); return; }

                ParameterInfo[] ps  = register.GetParameters();
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                foreach (ParameterInfo pi in ps) { sb.Append(pi.ParameterType.Name); sb.Append(' '); }
                Debug.Log("[LaneShifter] UUI overload: (" + sb.ToString().Trim() + ")");

                Texture2D   icon    = LoadIcon();
                // If icon is null, make solid green so UUI at least shows something visible
                if (object.ReferenceEquals(icon, null))
                {
                    icon = new Texture2D(40, 40, TextureFormat.RGBA32, false);
                    Color32 fill = new Color32(80, 200, 120, 255);
                    for (int y = 0; y < 40; y++) for (int x = 0; x < 40; x++) icon.SetPixel(x, y, fill);
                    icon.Apply();
                }
                Texture2D[] iconArr = new Texture2D[] { icon };

                object[] args = new object[ps.Length];
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
                    else if (typeof(ToolBase).IsAssignableFrom(ps[i].ParameterType))
                        args[i] = LaneShiftTool.Instance;
                    else if (string.Equals(fn, "UnityEngine.Texture2D[]", StringComparison.Ordinal))
                        args[i] = iconArr;
                    else if (string.Equals(fn, "UnityEngine.Texture2D", StringComparison.Ordinal))
                        args[i] = icon;
                    else
                        args[i] = null;
                }

                object result = register.Invoke(null, args);
                Debug.Log("[LaneShifter] UUI registered: " + result);
            }
            catch (Exception ex) { Debug.LogWarning("[LaneShifter] UUI error: " + ex); }
        }
    }
}
