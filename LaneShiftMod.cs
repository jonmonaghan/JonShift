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

        public void OnSettingsUI(UIHelperBase helper)
        {
            var cfg = LaneShiftSettings.Instance;

            // ---- Hotkey ----
            UIHelperBase hotkeyGroup = helper.AddGroup("Hotkey");

            // Modifier checkboxes
            hotkeyGroup.AddCheckbox("Ctrl",  cfg.HotkeyCtrl,  v => { cfg.HotkeyCtrl  = v; cfg.Save(); });
            hotkeyGroup.AddCheckbox("Shift", cfg.HotkeyShift, v => { cfg.HotkeyShift = v; cfg.Save(); });
            hotkeyGroup.AddCheckbox("Alt",   cfg.HotkeyAlt,   v => { cfg.HotkeyAlt   = v; cfg.Save(); });

            // Key dropdown — common keys
            string[] keys = new string[]
            {
                "(unbound)",
                "A","B","C","D","E","F","G","H","I","J","K","L","M",
                "N","O","P","Q","R","S","T","U","V","W","X","Y","Z",
                "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12",
                "Alpha1","Alpha2","Alpha3","Alpha4","Alpha5",
                "Alpha6","Alpha7","Alpha8","Alpha9","Alpha0",
                "Tab","BackQuote","Minus","Equals","LeftBracket","RightBracket",
                "Backslash","Semicolon","Quote","Comma","Period","Slash"
            };

            // Find current selection index
            int currentIdx = 0;
            if (cfg.HotkeyCode != (int)UnityEngine.KeyCode.None)
            {
                string currentName = ((UnityEngine.KeyCode)cfg.HotkeyCode).ToString();
                for (int i = 1; i < keys.Length; i++)
                {
                    if (string.Equals(keys[i], currentName, StringComparison.Ordinal))
                    {
                        currentIdx = i;
                        break;
                    }
                }
            }

            hotkeyGroup.AddDropdown("Key", keys, currentIdx, sel =>
            {
                if (sel == 0)
                    cfg.HotkeyCode = (int)UnityEngine.KeyCode.None;
                else
                {
                    try   { cfg.HotkeyCode = (int)Enum.Parse(typeof(UnityEngine.KeyCode), keys[sel]); }
                    catch { cfg.HotkeyCode = (int)UnityEngine.KeyCode.None; }
                }
                cfg.Save();
            });

            // ---- Button Visibility ----
            UIHelperBase visGroup = helper.AddGroup("Button Visibility");

            visGroup.AddCheckbox("Show in UnifiedUI toolbar", cfg.ShowInUUI, v =>
            {
                cfg.ShowInUUI = v;
                cfg.Save();
                // Live update not possible for UUI (registered at level load); takes effect on next load.
            });

            visGroup.AddCheckbox("Show standalone button on screen", cfg.ShowStandaloneButton, v =>
            {
                cfg.ShowStandaloneButton = v;
                cfg.Save();
                if (!object.ReferenceEquals(LaneShiftButton.Instance, null))
                    LaneShiftButton.Instance.isVisible = v;
            });
        }
    }

    // ---------------------------------------------------------------
    // Hotkey monitor — reads settings every frame, no hardcoded key
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
    // Loading extension
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
            if (mode != LoadMode.LoadGame && mode != LoadMode.NewGame)
                return;

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

            // Standalone button — always added, visibility controlled by settings
            UIView.GetAView().AddUIComponent(typeof(LaneShiftButton));

            // UUI — only register if setting is on
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

        public override void OnReleased()
        {
            _harmony?.UnpatchAll(HARMONY_ID);
        }

        // ---- UUI registration ----
        private static void TryRegisterWithUUI()
        {
            try
            {
                // Find UnifiedUILib assembly
                Type helpers = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Equals(asm.GetName().Name, "UnifiedUILib", StringComparison.Ordinal))
                    {
                        helpers = asm.GetType("UnifiedUI.Helpers.UUIHelpers");
                        break;
                    }
                }
                if (object.ReferenceEquals(helpers, null))
                {
                    Debug.Log("[LaneShifter] UUI not present.");
                    return;
                }

                // Pick the RegisterToolButton overload that takes Texture2D[] — NOT the string
                // icon-path overload.  We iterate all overloads, require a ToolBase parameter,
                // and prefer the one that also has a Texture2D[] parameter.
                MethodInfo bestWithTex  = null;
                MethodInfo bestNoTex    = null;

                foreach (MethodInfo m in helpers.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!string.Equals(m.Name, "RegisterToolButton", StringComparison.Ordinal))
                        continue;

                    bool hasToolBase = false;
                    bool hasTexArr   = false;

                    foreach (ParameterInfo p in m.GetParameters())
                    {
                        if (typeof(ToolBase).IsAssignableFrom(p.ParameterType))
                            hasToolBase = true;

                        // Texture2D[] FullName = "UnityEngine.Texture2D[]"
                        if (string.Equals(p.ParameterType.FullName,
                                          "UnityEngine.Texture2D[]",
                                          StringComparison.Ordinal))
                            hasTexArr = true;
                    }

                    if (!hasToolBase) continue;

                    if (hasTexArr)
                        bestWithTex = m;
                    else
                        bestNoTex   = m;
                }

                // Strongly prefer the Texture2D[] overload
                MethodInfo register = !object.ReferenceEquals(bestWithTex, null)
                    ? bestWithTex : bestNoTex;

                if (object.ReferenceEquals(register, null))
                {
                    Debug.LogWarning("[LaneShifter] UUI: no RegisterToolButton overload found.");
                    return;
                }

                // Log which overload we chose
                ParameterInfo[] ps  = register.GetParameters();
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                foreach (ParameterInfo pi in ps) { sb.Append(pi.ParameterType.Name); sb.Append(' '); }
                Debug.Log("[LaneShifter] UUI overload chosen: (" + sb.ToString().Trim() + ")");

                // Build args — use FullName comparisons only (no Type ==)
                object[]  args  = new object[ps.Length];
                Texture2D icon  = LoadIcon();
                Texture2D[] iconArr = new Texture2D[] { icon };
                int stringIdx = 0;

                for (int i = 0; i < ps.Length; i++)
                {
                    string ptFull = ps[i].ParameterType.FullName;

                    if (string.Equals(ptFull, "System.String", StringComparison.Ordinal))
                    {
                        // Strings in order: name, groupName, tooltip
                        if      (stringIdx == 0) args[i] = "LaneShifter";
                        else if (stringIdx == 1) args[i] = null; // groupName
                        else                     args[i] = "Lane Shifter"; // tooltip — NO parenthetical
                        stringIdx++;
                    }
                    else if (typeof(ToolBase).IsAssignableFrom(ps[i].ParameterType))
                    {
                        args[i] = LaneShiftTool.Instance;
                    }
                    else if (string.Equals(ptFull, "UnityEngine.Texture2D[]", StringComparison.Ordinal))
                    {
                        args[i] = iconArr;
                    }
                    else if (string.Equals(ptFull, "UnityEngine.Texture2D", StringComparison.Ordinal))
                    {
                        args[i] = icon;
                    }
                    else
                    {
                        args[i] = null;
                    }
                }

                object result = register.Invoke(null, args);
                Debug.Log("[LaneShifter] UUI registered OK: " + result);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LaneShifter] UUI error: " + ex);
            }
        }

        // ---- Icon: load from mod folder (next to the DLL) ----
        private static Texture2D LoadIcon()
        {
            try
            {
                string modFolder = System.IO.Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location);
                string iconPath = System.IO.Path.Combine(modFolder, "icon.png");

                if (System.IO.File.Exists(iconPath))
                {
                    byte[]    buf = System.IO.File.ReadAllBytes(iconPath);
                    Texture2D tex = new Texture2D(40, 40, TextureFormat.ARGB32, false);
                    tex.LoadImage(buf);
                    Debug.Log("[LaneShifter] icon loaded from " + iconPath);
                    return tex;
                }
                Debug.LogWarning("[LaneShifter] icon.png not found at " + iconPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LaneShifter] icon load error: " + ex.Message);
            }

            // Fallback: solid green square
            Texture2D fb   = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            Color32   fill = new Color32(80, 200, 120, 255);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    fb.SetPixel(x, y, fill);
            fb.Apply();
            return fb;
        }
    }
}
