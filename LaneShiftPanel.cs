using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.UI;
using UnityEngine;

namespace LaneShifter
{
    public class LaneShiftPanel : UIPanel
    {
        public static LaneShiftPanel Instance { get; private set; }

        private ushort _segmentId;
        private UILabel _titleLabel;
        private UIScrollablePanel _laneList;

        // Preset offsets shown as quick-pick buttons
        private static readonly float[] Presets = { 0.5f, 1.0f };

        // ---- Creation ----
        public static LaneShiftPanel Create()
        {
            UIView uiView = UIView.GetAView();
            Instance = uiView.AddUIComponent(typeof(LaneShiftPanel)) as LaneShiftPanel;
            return Instance;
        }

        public static void Destroy()
        {
            if (Instance != null)
            {
                DestroyImmediate(Instance.gameObject);
                Instance = null;
            }
        }

        // ---- UIComponent setup ----
        public override void Start()
        {
            base.Start();
            backgroundSprite = "MenuPanel2";
            opacity          = 0.95f;
            width            = 340f;
            height           = 60f;   // grows when lanes are added
            isVisible        = false;
            canFocus         = true;
            isInteractive    = true;
            relativePosition = new Vector3(Screen.width / 2f - 170f, Screen.height / 2f - 150f);

            // Drag support
            UIDragHandle drag = AddUIComponent<UIDragHandle>();
            drag.width  = width;
            drag.height = 40f;
            drag.relativePosition = Vector3.zero;
            drag.target = this;

            _titleLabel = AddUIComponent<UILabel>();
            _titleLabel.text             = "Lane Shifter";
            _titleLabel.textScale        = 0.9f;
            _titleLabel.font             = GetUIFont("OpenSans-Regular");
            _titleLabel.relativePosition = new Vector3(10f, 12f);

            // Close button
            UIButton close = AddUIComponent<UIButton>();
            close.width             = 24f;
            close.height            = 24f;
            close.normalBgSprite    = "buttonclose";
            close.hoveredBgSprite   = "buttonclosehover";
            close.pressedBgSprite   = "buttonclosepressed";
            close.relativePosition  = new Vector3(width - 30f, 8f);
            close.eventClicked     += (_, __) =>
            {
                Hide();
                _segmentId = 0;
                LaneShiftTool.HoveredLaneId_Static = 0;  // clear lane highlight
            };

            _laneList = AddUIComponent<UIScrollablePanel>();
            _laneList.width              = width - 20f;
            _laneList.height             = 300f;
            _laneList.relativePosition   = new Vector3(10f, 45f);
            _laneList.autoLayout         = true;
            _laneList.autoLayoutDirection= LayoutDirection.Vertical;
            _laneList.autoLayoutPadding  = new RectOffset(0, 0, 2, 2);
            _laneList.scrollWheelDirection = UIOrientation.Vertical;
            _laneList.clipChildren       = true;

            UIScrollbar sb = AddUIComponent<UIScrollbar>();
            sb.width             = 10f;
            sb.height            = 300f;
            sb.relativePosition  = new Vector3(width - 12f, 45f);
            sb.orientation       = UIOrientation.Vertical;
            sb.incrementAmount   = 20f;
            UISlicedSprite track = sb.AddUIComponent<UISlicedSprite>();
            track.spriteName     = "ScrollbarTrack";
            track.size           = sb.size;
            track.relativePosition = Vector3.zero;
            sb.trackObject       = track;
            UISlicedSprite thumb = track.AddUIComponent<UISlicedSprite>();
            thumb.spriteName     = "ScrollbarThumb";
            sb.thumbObject       = thumb;
            _laneList.verticalScrollbar = sb;
        }

        // ---- Public ----
        public void ShowForSegment(ushort segmentId)
        {
            _segmentId = segmentId;
            LaneShiftTool.HoveredLaneId_Static = 0;
            _titleLabel.text = $"Lane Shifter — Segment #{segmentId}";
            BuildLaneRows();
            Show();
        }

        // ---- Rebuild lane rows ----
        private void BuildLaneRows()
        {
            // Clear old rows
            var old = new List<UIComponent>(_laneList.components);
            foreach (var c in old) DestroyImmediate(c.gameObject);

            LaneShiftManager mgr = LaneShiftManager.Instance;
            if (mgr == null || _segmentId == 0) return;

            List<uint> lanes = LaneShiftManager.GetLaneIds(_segmentId);
            NetManager nm    = Singleton<NetManager>.instance;
            NetInfo info      = nm.m_segments.m_buffer[_segmentId].Info;

            for (int i = 0; i < lanes.Count; i++)
            {
                int   rowIndex = i;
                uint  laneId   = lanes[i];
                float curShift = mgr.GetShift(laneId);
                string laneType = info != null && i < info.m_lanes.Length
                    ? info.m_lanes[i].m_laneType.ToString()
                    : "Lane";

                // Row panel
                UIPanel row = _laneList.AddUIComponent<UIPanel>();
                row.width  = _laneList.width;
                row.height = 38f;
                row.backgroundSprite = "GenericPanel";
                row.color            = new Color32(50, 50, 50, 200);

                // Highlight this lane bezier on hover
                row.eventMouseEnter += (_, __) =>
                {
                    LaneShiftTool.HoveredLaneId_Static = laneId;
                    row.color = new Color32(70, 90, 70, 220);
                };
                row.eventMouseLeave += (_, __) =>
                {
                    LaneShiftTool.HoveredLaneId_Static = 0;
                    row.color = new Color32(50, 50, 50, 200);
                };

                // Lane label
                UILabel lbl = row.AddUIComponent<UILabel>();
                lbl.text             = $"#{i + 1} {laneType}";
                lbl.textScale        = 0.75f;
                lbl.relativePosition = new Vector3(6f, 12f);
                lbl.width            = 90f;

                // -1 button
                UIButton btnMinus = MakeSmallButton(row, "-1", new Vector3(100f, 6f));
                btnMinus.eventClicked += (_, __) => ApplyPreset(laneId, -1.0f);

                // -.5 button
                UIButton btnHalfM = MakeSmallButton(row, "-.5", new Vector3(132f, 6f));
                btnHalfM.eventClicked += (_, __) => ApplyPreset(laneId, -0.5f);

                // Editable offset field
                UITextField offsetField = row.AddUIComponent<UITextField>();
                offsetField.width             = 44f;
                offsetField.height            = 24f;
                offsetField.relativePosition  = new Vector3(169f, 7f);
                offsetField.text              = curShift.ToString("F2");
                offsetField.textScale         = 0.75f;
                offsetField.padding           = new RectOffset(4, 4, 5, 0);
                offsetField.builtinKeyNavigation = true;
                offsetField.isInteractive     = true;
                offsetField.readOnly          = false;
                offsetField.selectionSprite   = "EmptySprite";
                offsetField.normalBgSprite    = "TextFieldPanel";
                offsetField.hoveredBgSprite   = "TextFieldPanelHovered";
                offsetField.focusedBgSprite   = "TextFieldPanel";
                offsetField.color             = new Color32(30, 30, 30, 255);
                offsetField.textColor         = Color.white;
                offsetField.numericalOnly     = false;
                offsetField.allowFloats       = true;
                offsetField.submitOnFocusLost = true;

                // Capture laneId for the closure
                uint capturedLane = laneId;
                UITextField capturedField = offsetField;
                offsetField.eventTextSubmitted += (_, value) =>
                {
                    if (float.TryParse(value.Trim(), out float parsed))
                    {
                        mgr.SetShift(capturedLane, parsed);
                        LaneShiftManager.UpdateSegment(_segmentId);
                        capturedField.text = parsed.ToString("F2");
                    }
                };

                // +.5 button
                UIButton btnHalfP = MakeSmallButton(row, "+.5", new Vector3(218f, 6f));
                btnHalfP.eventClicked += (_, __) => ApplyPreset(laneId, 0.5f);

                // +1 button
                UIButton btnPlus = MakeSmallButton(row, "+1", new Vector3(252f, 6f));
                btnPlus.eventClicked += (_, __) => ApplyPreset(laneId, 1.0f);

                // Reset button
                UIButton btnReset = MakeSmallButton(row, "0", new Vector3(287f, 6f));
                btnReset.eventClicked += (_, __) =>
                {
                    mgr.SetShift(laneId, 0f);
                    LaneShiftManager.UpdateSegment(_segmentId);
                    // Refresh the text field via a full rebuild
                    BuildLaneRows();
                };

                void ApplyPreset(uint lid, float delta)
                {
                    float next = Mathf.Round((mgr.GetShift(lid) + delta) * 100f) / 100f;
                    mgr.SetShift(lid, next);
                    LaneShiftManager.UpdateSegment(_segmentId);
                    capturedField.text = next.ToString("F2");
                }
            }

            height = 50f + Mathf.Min(lanes.Count * 42f + 10f, 320f);
            _laneList.height = height - 55f;
        }

        // ---- Helpers ----
        private static UIButton MakeSmallButton(UIPanel parent, string text, Vector3 pos)
        {
            UIButton btn = parent.AddUIComponent<UIButton>();
            btn.width            = 30f;
            btn.height           = 24f;
            btn.text             = text;
            btn.textScale        = 0.65f;
            btn.normalBgSprite   = "ButtonMenu";
            btn.hoveredBgSprite  = "ButtonMenuHovered";
            btn.pressedBgSprite  = "ButtonMenuPressed";
            btn.textColor        = Color.white;
            btn.hoveredTextColor = Color.white;
            btn.relativePosition = pos;
            return btn;
        }

        private static UIFont GetUIFont(string name)
        {
            foreach (UIFont f in Resources.FindObjectsOfTypeAll<UIFont>())
                if (f.name == name) return f;
            return null;
        }
    }
}
