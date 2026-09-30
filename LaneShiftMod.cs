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
        public string Description => "Shift individual road lanes laterally. Hotkey: Shift+L";
    }

    // Always-on hotkey monitor
    public class LaneShiftHotkeyMonitor : MonoBehaviour
    {
        void Update()
        {
            if ((Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                && Input.GetKeyDown(KeyCode.L))
            {
                if (!object.ReferenceEquals(ToolsModifierControl.toolController, null)
                    && !object.ReferenceEquals(LaneShiftTool.Instance, null))
                {
                    if (object.ReferenceEquals(
                            ToolsModifierControl.toolController.CurrentTool,
                            LaneShiftTool.Instance))
                        LaneShiftTool.DisableTool();
                    else
                        LaneShiftTool.EnableTool();
                }
            }
        }
    }

    public class LaneShiftLoading : LoadingExtensionBase
    {
        private const string HARMONY_ID = "com.jonmonaghan.laneshifter";
        private Harmony    _harmony;
        private GameObject _hotkeyObj;
        private UIPanel    _floatPanel;

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

            // Always create the floating button; UUI will ALSO add its own
            // button if installed, giving two ways to activate the tool.
            _floatPanel = CreateFloatingButton();
            TryRegisterWithUUI();
        }

        public override void OnLevelUnloading()
        {
            LaneShiftPanel.Destroy();

            if (!object.ReferenceEquals(_floatPanel, null))
            {
                UnityEngine.Object.Destroy(_floatPanel.gameObject);
                _floatPanel = null;
            }

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

        // ---- UnifiedUI ----
        private static void TryRegisterWithUUI()
        {
            try
            {
                Type helpers = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Equals(asm.GetName().Name, "UnifiedUILib",
                                      StringComparison.Ordinal))
                    {
                        helpers = asm.GetType("UnifiedUI.Helpers.UUIHelpers");
                        break;
                    }
                }
                if (object.ReferenceEquals(helpers, null))
                {
                    Debug.Log("[LaneShifter] UnifiedUILib not found.");
                    return;
                }

                // Enumerate all overloads of RegisterToolButton and pick
                // the one that accepts a ToolBase parameter.
                MethodInfo register = null;
                MethodInfo[] methods = helpers.GetMethods(
                    BindingFlags.Public | BindingFlags.Static);
                foreach (MethodInfo m in methods)
                {
                    if (!string.Equals(m.Name, "RegisterToolButton",
                                       StringComparison.Ordinal))
                        continue;
                    ParameterInfo[] parms = m.GetParameters();
                    bool hasTool = false;
                    foreach (ParameterInfo p in parms)
                        if (typeof(ToolBase).IsAssignableFrom(p.ParameterType))
                            hasTool = true;
                    if (hasTool) { register = m; break; }
                }

                if (object.ReferenceEquals(register, null))
                {
                    Debug.LogWarning("[LaneShifter] No RegisterToolButton overload found.");
                    return;
                }

                // Build argument list to match whatever the method expects.
                ParameterInfo[] ps = register.GetParameters();
                object[] args = new object[ps.Length];
                Texture2D[] icons = new Texture2D[] { LoadIcon() };

                for (int i = 0; i < ps.Length; i++)
                {
                    Type pt = ps[i].ParameterType;
                    if (pt == typeof(string) && i == 0) args[i] = "LaneShifter";
                    else if (pt == typeof(string) && i == 1) args[i] = null; // groupName
                    else if (pt == typeof(string))           args[i] = "Lane Shifter (Shift+L)";
                    else if (typeof(ToolBase).IsAssignableFrom(pt)) args[i] = LaneShiftTool.Instance;
                    else if (pt == typeof(Texture2D[]))      args[i] = icons;
                    else if (pt == typeof(Texture2D))        args[i] = icons[0];
                    else                                     args[i] = null; // optional params
                }

                object result = register.Invoke(null, args);
                Debug.Log("[LaneShifter] UUI registration result: " + result);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LaneShifter] UUI registration error: " + ex);
            }
        }

        // ---- Floating button ----
        // Positioned in the top-left below the main toolbar (~60px from top)
        private static UIPanel CreateFloatingButton()
        {
            try
            {
                UIView view = UIView.GetAView();
                UIPanel wrapper = view.AddUIComponent<UIPanel>();
                wrapper.width            = 42f;
                wrapper.height           = 42f;
                wrapper.backgroundSprite = "GenericPanel";
                wrapper.opacity          = 0.9f;
                // Top-left corner, below the main toolbar
                wrapper.absolutePosition = new Vector3(12f, 62f);
                wrapper.tooltip          = "Lane Shifter (Shift+L)";
                wrapper.BringToFront();

                UIDragHandle drag = wrapper.AddUIComponent<UIDragHandle>();
                drag.width           = 42f;
                drag.height          = 42f;
                drag.relativePosition = Vector3.zero;
                drag.target          = wrapper;

                UIButton btn = wrapper.AddUIComponent<UIButton>();
                btn.width            = 36f;
                btn.height           = 36f;
                btn.relativePosition = new Vector3(3f, 3f);
                btn.tooltip          = "Lane Shifter (Shift+L)";
                btn.text             = "LS";
                btn.textScale        = 0.75f;
                btn.textColor        = Color.white;
                btn.normalBgSprite   = "OptionBase";
                btn.hoveredBgSprite  = "OptionBaseHovered";
                btn.pressedBgSprite  = "OptionBasePressed";
                btn.focusedBgSprite  = "OptionBaseFocused";

                btn.eventClicked += (_, __) =>
                {
                    if (object.ReferenceEquals(LaneShiftTool.Instance, null)) return;
                    if (object.ReferenceEquals(ToolsModifierControl.toolController, null)) return;
                    if (object.ReferenceEquals(
                            ToolsModifierControl.toolController.CurrentTool,
                            LaneShiftTool.Instance))
                        LaneShiftTool.DisableTool();
                    else
                        LaneShiftTool.EnableTool();
                };

                Debug.Log("[LaneShifter] Floating button created at top-left.");
                return wrapper;
            }
            catch (Exception ex)
            {
                Debug.LogError("[LaneShifter] Floating button error: " + ex);
                return null;
            }
        }

        // ---- Icon ----
        private static Texture2D LoadIcon()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                Stream s = asm.GetManifestResourceStream("LaneShifter.icon.png");
                if (!object.ReferenceEquals(s, null))
                {
                    using (s)
                    {
                        byte[] buf = new byte[s.Length];
                        s.Read(buf, 0, buf.Length);
                        Texture2D tex = new Texture2D(40, 40, TextureFormat.ARGB32, false);
                        tex.LoadImage(buf);
                        return tex;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LaneShifter] icon.png: " + ex.Message);
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
