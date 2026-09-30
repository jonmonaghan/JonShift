using HarmonyLib;
using ICities;
using UnityEngine;

namespace JonShift
{
    public class LaneShiftMod : IUserMod
    {
        public string Name        => "JonShift - Lane Offset Tool";
        public string Description => "Click any road segment to shift individual lanes left or right. Changes saved with the map.";

        private const string HARMONY_ID = "com.jonshift.laneshift";
        private static Harmony _harmony;

        // Called when the mod is enabled in the content manager.
        public void OnEnabled()
        {
            _harmony = new Harmony(HARMONY_ID);
            _harmony.PatchAll();
            Debug.Log("[JonShift] Harmony patches applied.");
        }

        public void OnDisabled()
        {
            _harmony?.UnpatchAll(HARMONY_ID);
            Debug.Log("[JonShift] Harmony patches removed.");
        }
    }

    /// <summary>
    /// Hooks into the game's loading lifecycle to set up the manager, tool, and panel.
    /// </summary>
    public class LaneShiftLoading : LoadingExtensionBase
    {
        public override void OnLevelLoaded(LoadMode mode)
        {
            if (mode != LoadMode.LoadGame && mode != LoadMode.NewGame &&
                mode != LoadMode.NewGameFromScenario && mode != LoadMode.LoadScenario)
                return;

            LaneShiftManager.Create();
            LaneShiftTool.Create();
            LaneShiftPanel.Create();
            CreateToolbarButton();

            Debug.Log("[JonShift] Level loaded — manager, tool, and panel ready.");
        }

        public override void OnLevelUnloading()
        {
            LaneShiftPanel.Destroy();
            LaneShiftTool.Remove();
            LaneShiftManager.Release();
            Debug.Log("[JonShift] Level unloaded.");
        }

        // ---- Toolbar button ----
        // Adds a button to the Roads panel toolbar so the user can activate the tool.
        private static void CreateToolbarButton()
        {
            try
            {
                // Find the roads panel button strip (UIPanelBase inside the main toolbar).
                // We create a standalone button anchored to the screen instead — simpler and more reliable.
                var uiView = ColossalFramework.UI.UIView.GetAView();
                var btnGo  = new UnityEngine.GameObject("JonShiftToolbarBtn");
                btnGo.transform.SetParent(uiView.transform, false);

                var btn = btnGo.AddComponent<ColossalFramework.UI.UIButton>();
                btn.text            = "🔄 Lane Shift";
                btn.width           = 110f;
                btn.height          = 30f;
                btn.textScale       = 0.78f;
                btn.textColor       = Color.white;
                btn.normalBgSprite  = "ButtonMenu";
                btn.hoveredBgSprite = "ButtonMenuHovered";
                btn.pressedBgSprite = "ButtonMenuPressed";
                btn.absolutePosition = new Vector3(10f, 60f);   // top-left corner
                btn.tooltip         = "Activate Lane Shift tool (Esc to deactivate)";

                bool active = false;
                btn.eventClicked += (_, __) =>
                {
                    active = !active;
                    if (active)
                    {
                        LaneShiftTool.EnableTool();
                        btn.textColor = new Color32(80, 255, 140, 255);
                    }
                    else
                    {
                        LaneShiftTool.DisableTool();
                        btn.textColor = Color.white;
                    }
                };

                Debug.Log("[JonShift] Toolbar button created.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[JonShift] CreateToolbarButton failed: " + ex);
            }
        }
    }
}
