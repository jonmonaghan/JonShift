using ColossalFramework;
using ColossalFramework.UI;
using UnityEngine;

namespace JonShift
{
    /// <summary>
    /// Floating UI panel that lists lanes for the selected segment.
    /// Each lane gets +0.5 / +0.25 / label / -0.25 / -0.5 / Reset buttons.
    /// </summary>
    public class LaneShiftPanel : UIPanel
    {
        public static LaneShiftPanel Instance { get; private set; }

        private UILabel  _titleLabel;
        private UIButton _closeButton;
        private UIScrollablePanel _laneContainer;

        private ushort _currentSegment;

        // ---- Creation ----
        public static LaneShiftPanel Create()
        {
            UIView uiView = UIView.GetAView();
            var go = new GameObject("JonShiftPanel");
            go.transform.SetParent(uiView.transform, false);
            Instance = go.AddComponent<LaneShiftPanel>();
            return Instance;
        }

        public static void Destroy()
        {
            if (Instance != null)
            {
                GameObject.Destroy(Instance.gameObject);
                Instance = null;
            }
        }

        // ---- UIComponent lifecycle ----
        public override void Start()
        {
            base.Start();
            BuildUI();
            Hide();
        }

        private void BuildUI()
        {
            // Root panel
            backgroundSprite = "MenuPanel2";
            color            = new Color32(58, 68, 84, 255);
            width            = 360f;
            height           = 60f; // grows dynamically
            canFocus         = true;
            isInteractive    = true;
            clipChildren     = true;
            absolutePosition = new Vector3(200, 200);

            // Drag support
            UIDragHandle drag = AddUIComponent<UIDragHandle>();
            drag.width  = width;
            drag.height = 32f;
            drag.relativePosition = Vector3.zero;
            drag.target = this;

            // Title
            _titleLabel = AddUIComponent<UILabel>();
            _titleLabel.text             = "Lane Shift";
            _titleLabel.textScale        = 0.9f;
            _titleLabel.textColor        = Color.white;
            _titleLabel.relativePosition = new Vector3(10, 8);

            // Close button
            _closeButton = AddUIComponent<UIButton>();
            _closeButton.text             = "X";
            _closeButton.width            = 22f;
            _closeButton.height           = 22f;
            _closeButton.textScale        = 0.85f;
            _closeButton.normalBgSprite   = "ButtonSmall";
            _closeButton.hoveredBgSprite  = "ButtonSmallHovered";
            _closeButton.pressedBgSprite  = "ButtonSmallPressed";
            _closeButton.textColor        = Color.white;
            _closeButton.relativePosition = new Vector3(width - 28f, 5f);
            _closeButton.eventClicked    += (_, __) => Hide();

            // Scrollable lane list
            _laneContainer = AddUIComponent<UIScrollablePanel>();
            _laneContainer.width            = width - 10f;
            _laneContainer.relativePosition = new Vector3(5f, 38f);
            _laneContainer.autoLayout       = true;
            _laneContainer.autoLayoutDirection = LayoutDirection.Vertical;
            _laneContainer.autoLayoutPadding   = new RectOffset(0, 0, 2, 2);
        }

        // ---- Public API ----
        public void ShowForSegment(ushort segmentId)
        {
            _currentSegment = segmentId;
            RebuildLaneRows();
            Show();
        }

        // ---- Internal ----
        private void RebuildLaneRows()
        {
            // Clear existing rows
            while (_laneContainer.components.Count > 0)
                DestroyImmediate(_laneContainer.components[0].gameObject);

            NetManager nm = Singleton<NetManager>.instance;
            ref NetSegment seg = ref nm.m_segments.m_buffer[_currentSegment];
            if (seg.Info == null) return;

            _titleLabel.text = $"Lane Shift — Seg #{_currentSegment}";

            uint laneId    = seg.m_lanes;
            NetInfo.Lane[] laneInfos = seg.Info.m_lanes;
            int rowCount   = 0;

            for (int i = 0; i < laneInfos.Length && laneId != 0; i++)
            {
                NetInfo.Lane info = laneInfos[i];
                uint capturedLaneId = laneId;
                float currentShift  = LaneShiftManager.Instance?.GetShift(capturedLaneId) ?? 0f;

                AddLaneRow(_laneContainer, i, info, capturedLaneId, currentShift);
                rowCount++;

                laneId = nm.m_lanes.m_buffer[laneId].m_nextLane;
            }

            // Resize panel height
            float rowH  = 34f;
            float pad   = 4f;
            height = 42f + rowCount * (rowH + pad) + 8f;
            _laneContainer.height = height - 42f;
        }

        private void AddLaneRow(UIScrollablePanel parent, int index, NetInfo.Lane info, uint laneId, float currentShift)
        {
            // Row panel
            UIPanel row = parent.AddUIComponent<UIPanel>();
            row.width           = parent.width;
            row.height          = 34f;
            row.backgroundSprite = "GenericPanel";
            row.color           = new Color32(40, 50, 65, 220);

            // Lane label
            UILabel lbl = row.AddUIComponent<UILabel>();
            lbl.text             = $"[{index}] {info.m_laneType} pos:{info.m_position:F2}";
            lbl.textScale        = 0.72f;
            lbl.textColor        = Color.white;
            lbl.autoSize         = false;
            lbl.width            = 160f;
            lbl.height           = 34f;
            lbl.verticalAlignment   = UIVerticalAlignment.Middle;
            lbl.relativePosition    = new Vector3(4f, 0f);

            // Shift value display
            UILabel shiftVal = row.AddUIComponent<UILabel>();
            shiftVal.text          = FormatShift(currentShift);
            shiftVal.textScale     = 0.78f;
            shiftVal.textColor     = ShiftColor(currentShift);
            shiftVal.autoSize      = false;
            shiftVal.width         = 42f;
            shiftVal.height        = 34f;
            shiftVal.textAlignment = UIHorizontalAlignment.Center;
            shiftVal.verticalAlignment = UIVerticalAlignment.Middle;
            shiftVal.relativePosition  = new Vector3(162f, 0f);

            // Buttons: -0.5  -0.25  +0.25  +0.5  Reset
            float bx = 206f;
            MakeShiftButton(row, "-½",  bx,       laneId, shiftVal, -0.5f);
            MakeShiftButton(row, "-¼",  bx + 28f,  laneId, shiftVal, -0.25f);
            MakeShiftButton(row, "+¼",  bx + 56f,  laneId, shiftVal, +0.25f);
            MakeShiftButton(row, "+½",  bx + 84f,  laneId, shiftVal, +0.5f);
            MakeResetButton(row, "R",  bx + 112f, laneId, shiftVal);
        }

        private void MakeShiftButton(UIPanel row, string label, float x, uint laneId, UILabel display, float delta)
        {
            UIButton btn = row.AddUIComponent<UIButton>();
            btn.text            = label;
            btn.width           = 26f;
            btn.height          = 26f;
            btn.textScale       = 0.70f;
            btn.textColor       = Color.white;
            btn.normalBgSprite  = "ButtonSmall";
            btn.hoveredBgSprite = "ButtonSmallHovered";
            btn.pressedBgSprite = "ButtonSmallPressed";
            btn.relativePosition = new Vector3(x, 4f);
            btn.eventClicked   += (_, __) =>
            {
                if (LaneShiftManager.Instance == null) return;
                float newShift = LaneShiftManager.Instance.GetShift(laneId) + delta;
                // Clamp to reasonable range
                newShift = Mathf.Clamp(newShift, -8f, 8f);
                LaneShiftManager.Instance.SetShift(laneId, newShift);
                LaneShiftManager.UpdateSegment(_currentSegment);
                display.text  = FormatShift(newShift);
                display.textColor = ShiftColor(newShift);
            };
        }

        private void MakeResetButton(UIPanel row, string label, float x, uint laneId, UILabel display)
        {
            UIButton btn = row.AddUIComponent<UIButton>();
            btn.text            = label;
            btn.width           = 22f;
            btn.height          = 26f;
            btn.textScale       = 0.70f;
            btn.textColor       = new Color32(255, 160, 80, 255);
            btn.normalBgSprite  = "ButtonSmall";
            btn.hoveredBgSprite = "ButtonSmallHovered";
            btn.pressedBgSprite = "ButtonSmallPressed";
            btn.relativePosition = new Vector3(x, 4f);
            btn.eventClicked   += (_, __) =>
            {
                if (LaneShiftManager.Instance == null) return;
                LaneShiftManager.Instance.SetShift(laneId, 0f);
                LaneShiftManager.UpdateSegment(_currentSegment);
                display.text  = FormatShift(0f);
                display.textColor = ShiftColor(0f);
            };
        }

        private static string FormatShift(float v) => v.ToString("+0.00;-0.00;0.00");
        private static Color  ShiftColor(float v)  =>
            Mathf.Approximately(v, 0f) ? Color.gray :
            v > 0 ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.55f, 0.4f);
    }
}
