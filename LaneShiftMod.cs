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
        public string Description => "Shift individual road lanes laterally. Hotkey: Shift+L";
    }

    // Always-on hotkey monitor (active even when Lane Shifter tool is not selected)
    public class LaneShiftHotkeyMonitor : MonoBehaviour
    {
        void Update()
        {
            if ((Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                && Input.GetKeyDown(KeyCode.L))
            {
                if (object.ReferenceEquals(ToolsModifierControl.toolController, null)) return;
                if (object.ReferenceEquals(LaneShiftTool.Instance, null)) return;

                if (object.ReferenceEquals(
                        ToolsModifierControl.toolController.CurrentTool,
                        LaneShiftTool.Instance))
                    LaneShiftTool.DisableTool();
                else
                    LaneShiftTool.EnableTool();
            }
        }
    }

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

            // Hotkey monitor
            _hotkeyObj = new GameObject("LaneShifterHotkey");
            UnityEngine.Object.DontDestroyOnLoad(_hotkeyObj);
            _hotkeyObj.AddComponent<LaneShiftHotkeyMonitor>();

            // Standalone button: always created, same pattern as ThemeMixer's UIToggle
            UIView.GetAView().AddUIComponent(typeof(LaneShiftButton));

            // Also try UUI so it shows up there too if installed
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

        // ---- UnifiedUI (optional) ----
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
                if (object.ReferenceEquals(helpers, null))
                {
                    Debug.Log("[LaneShifter] UUI not present.");
                    return;
                }

                // Find RegisterToolButton overload that accepts a ToolBase
                MethodInfo register = null;
                foreach (MethodInfo m in helpers.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!string.Equals(m.Name, "RegisterToolButton", StringComparison.Ordinal))
                        continue;
                    foreach (ParameterInfo p in m.GetParameters())
                    {
                        if (typeof(ToolBase).IsAssignableFrom(p.ParameterType))
                        {
                            register = m;
                            break;
                        }
                    }
                    if (!object.ReferenceEquals(register, null)) break;
                }

                if (object.ReferenceEquals(register, null))
                {
                    Debug.LogWarning("[LaneShifter] UUI: RegisterToolButton not found.");
                    return;
                }

                // Build args list matching whatever overload was found.
                // Use FullName string comparisons instead of == to avoid
                // Type.op_Equality which doesn't exist in CS1's Mono runtime.
                ParameterInfo[] ps    = register.GetParameters();
                object[]        args  = new object[ps.Length];
                Texture2D[]     icons = new Texture2D[] { LoadIcon() };

                for (int i = 0; i < ps.Length; i++)
                {
                    string ptName = ps[i].ParameterType.FullName;
                    if (string.Equals(ptName, "System.String", StringComparison.Ordinal) && i == 0)
                        args[i] = "LaneShifter";
                    else if (string.Equals(ptName, "System.String", StringComparison.Ordinal) && i == 1)
                        args[i] = null;
                    else if (string.Equals(ptName, "System.String", StringComparison.Ordinal))
                        args[i] = "Lane Shifter (Shift+L)";
                    else if (typeof(ToolBase).IsAssignableFrom(ps[i].ParameterType))
                        args[i] = LaneShiftTool.Instance;
                    else if (string.Equals(ptName, "UnityEngine.Texture2D[]", StringComparison.Ordinal))
                        args[i] = icons;
                    else if (string.Equals(ptName, "UnityEngine.Texture2D", StringComparison.Ordinal))
                        args[i] = icons[0];
                    else
                        args[i] = null;
                }

                // Log the full method signature to help debug icon issues
                ParameterInfo[] dbg = register.GetParameters();
                string sig = "";
                for (int d = 0; d < dbg.Length; d++) sig += dbg[d].ParameterType.Name + " ";
                Debug.Log("[LaneShifter] Calling UUI: " + register.Name + "(" + sig.Trim() + ")");

                object result = register.Invoke(null, args);
                Debug.Log("[LaneShifter] UUI registered: " + result);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LaneShifter] UUI error: " + ex);
            }
        }

        // ---- Icon ----
        // Loads icon.png from the same folder as the DLL (standard CS1 pattern).
        // Copy icon.png next to LaneShifter.dll in your Mods folder.
        private static Texture2D LoadIcon()
        {
            try
            {
                string modFolder = System.IO.Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location);
                string iconPath  = System.IO.Path.Combine(modFolder, "icon.png");

                if (System.IO.File.Exists(iconPath))
                {
                    byte[]    buf = System.IO.File.ReadAllBytes(iconPath);
                    Texture2D tex = new Texture2D(40, 40, TextureFormat.ARGB32, false);
                    tex.LoadImage(buf);
                    Debug.Log("[LaneShifter] Icon loaded from: " + iconPath);
                    return tex;
                }
                Debug.LogWarning("[LaneShifter] icon.png not found at: " + iconPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LaneShifter] Icon load error: " + ex.Message);
            }

            // Fallback: solid green square
            Texture2D fallback = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            Color32   fill     = new Color32(80, 200, 120, 255);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    fallback.SetPixel(x, y, fill);
            fallback.Apply();
            return fallback;
        }
    }
}
