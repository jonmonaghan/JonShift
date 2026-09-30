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
        public string Description => "Shift individual road lanes laterally on any segment.";
    }

    public class LaneShiftLoading : LoadingExtensionBase
    {
        private const string HARMONY_ID = "com.jonmonaghan.laneshifter";
        private Harmony  _harmony;
        private UIButton _toolbarButton;

        public override void OnCreated(ILoading loading)
        {
            _harmony = new Harmony(HARMONY_ID);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        public override void OnLevelLoaded(LoadMode mode)
        {
            // CS1 valid modes: LoadGame, NewGame, LoadMap, NewMap
            if (mode != LoadMode.LoadGame && mode != LoadMode.NewGame)
                return;

            LaneShiftManager.Create();

            // Restore saved shifts (OnLoadData fires before this method)
            if (LaneShiftManager.PendingLoadData != null)
            {
                LaneShiftManager.Instance.Deserialize(LaneShiftManager.PendingLoadData);
                LaneShiftManager.PendingLoadData = null;
            }

            LaneShiftTool.Create();
            LaneShiftPanel.Create();

            if (!TryRegisterWithUUI())
                _toolbarButton = AddStandaloneButton();
        }

        public override void OnLevelUnloading()
        {
            LaneShiftPanel.Destroy();

            if (_toolbarButton != null)
            {
                UnityEngine.Object.Destroy(_toolbarButton.gameObject);
                _toolbarButton = null;
            }

            LaneShiftTool.Remove();
            LaneShiftManager.Release();
        }

        public override void OnReleased()
        {
            _harmony?.UnpatchAll(HARMONY_ID);
        }

        // ---- UnifiedUI (optional, detected via reflection) ----
        private static bool TryRegisterWithUUI()
        {
            try
            {
                Assembly uui = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "UnifiedUILib")
                    {
                        uui = asm;
                        break;
                    }
                }
                if (uui == null) return false;

                Type helpers = uui.GetType("UnifiedUI.Helpers.UUIHelpers");
                if (helpers == null) return false;

                MethodInfo register = helpers.GetMethod(
                    "RegisterToolButton",
                    new[] { typeof(string), typeof(string), typeof(string),
                            typeof(ToolBase), typeof(Texture2D) });
                if (register == null) return false;

                Texture2D icon = LoadIcon();
                register.Invoke(null, new object[]
                {
                    "LaneShifter",
                    null,
                    "Lane Shifter",
                    LaneShiftTool.Instance,
                    icon
                });

                Debug.Log("[LaneShifter] Registered with UnifiedUI.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LaneShifter] UUI registration skipped: " + ex.Message);
                return false;
            }
        }

        // ---- Standalone toolbar button (fallback when UUI not present) ----
        private static UIButton AddStandaloneButton()
        {
            UITabstrip strip = ToolsModifierControl.mainToolbar
                               .GetComponentInChildren<UITabstrip>();
            if (strip == null) return null;

            UIButton btn = strip.AddTab("LaneShifter", null, false) as UIButton;
            if (btn == null) return null;

            btn.tooltip          = "Lane Shifter";
            btn.normalFgSprite   = "ToolbarIconProps";
            btn.focusedFgSprite  = "ToolbarIconPropsPressed";
            btn.hoveredFgSprite  = "ToolbarIconPropsHovered";
            btn.pressedFgSprite  = "ToolbarIconPropsPressed";
            btn.eventClicked    += (_, __) => LaneShiftTool.EnableTool();
            return btn;
        }

        // ---- Icon: embedded resource or fallback green square ----
        private static Texture2D LoadIcon()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                Stream s = asm.GetManifestResourceStream("LaneShifter.icon.png");
                if (s != null)
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
                Debug.LogWarning("[LaneShifter] Could not load icon.png: " + ex.Message);
            }
            return CreateFallbackIcon();
        }

        private static Texture2D CreateFallbackIcon()
        {
            Texture2D tex  = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            Color32   fill = new Color32(80, 200, 120, 255);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    tex.SetPixel(x, y, fill);
            tex.Apply();
            return tex;
        }
    }
}
