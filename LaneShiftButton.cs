using ColossalFramework.UI;
using UnityEngine;

namespace LaneShifter
{
    // A standalone UIComponent added directly to UIView — same pattern as
    // ThemeMixer's UIToggle. Visible whenever in-game; draggable.
    public class LaneShiftButton : UIButton
    {
        public static LaneShiftButton Instance { get; private set; }

        private UIDragHandle _drag;
        private bool _toolActive;

        public override void Start()
        {
            base.Start();
            Instance = this;

            name             = "LaneShifterButton";
            width            = 36f;
            height           = 36f;
            tooltip          = "Lane Shifter (Shift+L)";
            playAudioEvents  = true;

            // Use standard game sprites so no custom atlas is needed
            normalBgSprite   = "OptionBase";
            hoveredBgSprite  = "OptionBaseHovered";
            pressedBgSprite  = "OptionBasePressed";
            focusedBgSprite  = "OptionBaseFocused";
            disabledBgSprite = "OptionBaseDisabled";

            text      = "LS";
            textScale = 0.7f;
            textColor = Color.white;
            hoveredTextColor  = Color.white;
            pressedTextColor  = Color.white;
            focusedTextColor  = Color.white;

            // Bottom-left of screen, above the main toolbar row
            Vector2 res = GetUIView().GetScreenResolution();
            absolutePosition = new Vector3(12f, res.y - 120f);

            // Drag handle so the user can reposition it
            _drag = AddUIComponent<UIDragHandle>();
            _drag.width           = width;
            _drag.height          = height;
            _drag.relativePosition = Vector3.zero;
            _drag.target          = this;

            eventClicked += OnClicked;
        }

        private void OnClicked(UIComponent component, UIMouseEventParameter p)
        {
            if (object.ReferenceEquals(LaneShiftTool.Instance, null)) return;
            if (object.ReferenceEquals(ToolsModifierControl.toolController, null)) return;

            if (object.ReferenceEquals(
                    ToolsModifierControl.toolController.CurrentTool,
                    LaneShiftTool.Instance))
                LaneShiftTool.DisableTool();
            else
                LaneShiftTool.EnableTool();
        }

        public override void Update()
        {
            base.Update();
            // Keep the button visually toggled when tool is active
            bool active = !object.ReferenceEquals(LaneShiftTool.Instance, null)
                       && !object.ReferenceEquals(ToolsModifierControl.toolController, null)
                       && object.ReferenceEquals(
                              ToolsModifierControl.toolController.CurrentTool,
                              LaneShiftTool.Instance);

            if (active != _toolActive)
            {
                _toolActive = active;
                normalBgSprite  = active ? "OptionBaseFocused" : "OptionBase";
                focusedBgSprite = active ? "OptionBaseFocused" : "OptionBase";
            }
        }

        public override void OnDestroy()
        {
            if (object.ReferenceEquals(Instance, this)) Instance = null;
            base.OnDestroy();
        }
    }
}
