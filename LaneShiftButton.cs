// Stripped back to exactly what worked in fix4 — no atlas, no UITextureAtlas.SpriteInfo.
using ColossalFramework.UI;
using UnityEngine;

namespace LaneShifter
{
    public class LaneShiftButton : UIButton
    {
        public static LaneShiftButton Instance { get; private set; }
        private bool _toolActive;

        public override void Start()
        {
            base.Start();
            Instance = this;

            name             = "LaneShifterButton";
            width            = 36f;
            height           = 36f;
            tooltip          = "Lane Shifter";
            playAudioEvents  = true;
            normalBgSprite   = "OptionBase";
            hoveredBgSprite  = "OptionBaseHovered";
            pressedBgSprite  = "OptionBasePressed";
            focusedBgSprite  = "OptionBaseFocused";
            disabledBgSprite = "OptionBaseDisabled";
            text             = "LS";
            textScale        = 0.7f;
            textColor        = Color.white;
            hoveredTextColor = Color.white;
            pressedTextColor = Color.white;
            focusedTextColor = Color.white;

            Vector2 res = GetUIView().GetScreenResolution();
            absolutePosition = new Vector3(12f, res.y - 120f);

            UIDragHandle drag = AddUIComponent<UIDragHandle>();
            drag.width            = width;
            drag.height           = height;
            drag.relativePosition = Vector3.zero;
            drag.target           = this;

            isVisible = LaneShiftSettings.ShowStandaloneButton;

            eventClicked += (c, p) =>
            {
                if (object.ReferenceEquals(LaneShiftTool.Instance, null)) return;
                if (object.ReferenceEquals(ToolsModifierControl.toolController, null)) return;
                if (object.ReferenceEquals(ToolsModifierControl.toolController.CurrentTool, LaneShiftTool.Instance))
                    LaneShiftTool.DisableTool();
                else
                    LaneShiftTool.EnableTool();
            };
        }

        public override void Update()
        {
            base.Update();
            bool active = !object.ReferenceEquals(LaneShiftTool.Instance, null)
                       && !object.ReferenceEquals(ToolsModifierControl.toolController, null)
                       && object.ReferenceEquals(
                              ToolsModifierControl.toolController.CurrentTool,
                              LaneShiftTool.Instance);
            if (active != _toolActive)
            {
                _toolActive     = active;
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
