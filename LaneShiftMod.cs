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
        public string Description => "Shift individual road lanes laterally on any segment.";
    }

    public class LaneShiftLoading : LoadingExtensionBase
    {
        private const string HARMONY_ID = "com.jonmonaghan.laneshifter";
        private Harmony _harmony;
        private UIButton _toolbarButton; // standalone button (used only if UUI unavailable)

        public override void OnCreated(ILoading loading)
        {
            _harmony = new Harmony(HARMONY_ID);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        public override void OnLevelLoaded(LoadMode mode)
        {
            if (mode != LoadMode.LoadGame && mode != LoadMode.NewGame
                && mode != LoadMode.LoadScenario && mode != LoadMode.NewScenario)
                return;

            LaneShiftManager.Create();

            // ---- Restore saved shifts (OnLoadData fired before this) ----
            if (LaneShiftManager.PendingLoadData != null)
            {
                LaneShiftManager.Instance.Deserialize(LaneShiftManager.PendingLoadData);
                LaneShiftManager.PendingLoadData = null;
            }

            LaneShiftTool.Create();
            LaneShiftPanel.Create();

            // ---- Register with UnifiedUI if available; else add standalone button ----
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

        // ---- UnifiedUI ----
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

                // UnifiedUI.Helpers.UUIHelpers.RegisterToolButton(
                //     string name, string groupName, string tooltip,
                //     ToolBase tool, Texture2D icon)
                Type helpers = uui.GetType("UnifiedUI.Helpers.UUIHelpers");
                if (helpers == null) return false;

                MethodInfo register = helpers.GetMethod(
                    "RegisterToolButton",
                    new[] { typeof(string), typeof(string), typeof(string),
                            typeof(ToolBase), typeof(Texture2D) });
                if (register == null) return false;

                Texture2D icon = CreateFallbackIcon();
                register.Invoke(null, new object[]
                {
                    "LaneShifter",   // name
                    null,            // groupName (UUI will place it in the default group)
                    "Lane Shifter",  // tooltip
                    LaneShiftTool.Instance,
                    icon
                });

                Debug.Log("[LaneShifter] Registered with UnifiedUI.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LaneShifter] UUI registration failed, using standalone button: " + ex.Message);
                return false;
            }
        }

        // ---- Standalone toolbar button (fallback) ----
        private static UIButton AddStandaloneButton()
        {
            UITabstrip strip = ToolsModifierControl.mainToolbar
                               .GetComponentInChildren<UITabstrip>();
            if (strip == null) return null;

            UIButton btn = strip.AddTab("LaneShifter", null, false) as UIButton;
            if (btn == null) return null;

            btn.tooltip      = "Lane Shifter";
            btn.normalFgSprite   = "ToolbarIconProps"; // generic fallback icon
            btn.focusedFgSprite  = "ToolbarIconPropsPressed";
            btn.hoveredFgSprite  = "ToolbarIconPropsHovered";
            btn.pressedFgSprite  = "ToolbarIconPropsPressed";
            btn.eventClicked    += (_, __) => LaneShiftTool.EnableTool();

            return btn;
        }

        // ---- Simple 32x32 coloured icon ----
        private static Texture2D CreateFallbackIcon()
        {
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            var fill = new Color32(80, 200, 120, 255);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    tex.SetPixel(x, y, fill);
            tex.Apply();
            return tex;
        }
    }
}
