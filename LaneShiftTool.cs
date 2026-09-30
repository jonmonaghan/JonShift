using ColossalFramework;
using ColossalFramework.Math;
using ColossalFramework.UI;
using UnityEngine;

namespace LaneShifter
{
    public class LaneShiftTool : DefaultTool
    {
        public static LaneShiftTool Instance { get; private set; }

        public ushort HoveredSegmentId  { get; private set; }
        public ushort SelectedSegmentId { get; private set; }

        // Written by the panel when the mouse enters a lane row; read in RenderOverlay.
        public static uint HoveredLaneId_Static;

        private static readonly Color32 HoverColor    = new Color32(0,   181, 255, 180);
        private static readonly Color32 SelectedColor = new Color32(255, 200, 0,   180);
        private static readonly Color32 LaneColor     = new Color32(80,  255, 140, 220);

        // ---- Lifecycle ----
        public static void Create()
        {
            if (Instance != null) return;
            ToolController tc = FindObjectOfType<ToolController>();
            Instance = tc.gameObject.AddComponent<LaneShiftTool>();
        }

        public static void Remove()
        {
            if (Instance == null) return;
            Destroy(Instance);
            Instance = null;
        }

        protected override void Awake()
        {
            base.Awake();
            m_toolController = FindObjectOfType<ToolController>();
        }

        public static void EnableTool()
        {
            if (Instance == null) Create();
            ToolsModifierControl.toolController.CurrentTool = Instance;
        }

        public static void DisableTool()
        {
            ToolsModifierControl.toolController.CurrentTool =
                ToolsModifierControl.GetTool<DefaultTool>();
        }

        // ---- Tool updates ----
        protected override void OnToolUpdate()
        {
            base.OnToolUpdate();

            // Right-click or Escape — deactivate and close panel.
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                LaneShiftPanel.Instance?.Hide();
                SelectedSegmentId   = 0;
                HoveredLaneId_Static = 0;
                DisableTool();
                return;
            }

            HoveredSegmentId = GetHoveredSegment();

            // Left-click on segment (not inside UI) — select it.
            if (Input.GetMouseButtonDown(0) && !UIView.IsInsideUI())
            {
                if (HoveredSegmentId != 0)
                {
                    SelectedSegmentId = HoveredSegmentId;
                    LaneShiftPanel.Instance?.ShowForSegment(SelectedSegmentId);
                }
            }

            if (HoveredSegmentId != 0)
                ShowToolInfo(true, $"Segment #{HoveredSegmentId} — click to edit lanes", Vector3.zero);
            else
                ShowToolInfo(false, null, Vector3.zero);
        }

        // ---- Rendering ----
        public override void RenderOverlay(RenderManager.CameraInfo cameraInfo)
        {
            // Hovered segment (blue)
            if (HoveredSegmentId != 0 && HoveredSegmentId != SelectedSegmentId)
                NetTool.RenderOverlay(cameraInfo,
                    ref Singleton<NetManager>.instance.m_segments.m_buffer[HoveredSegmentId],
                    HoverColor, HoverColor);

            // Selected segment (yellow)
            if (SelectedSegmentId != 0)
                NetTool.RenderOverlay(cameraInfo,
                    ref Singleton<NetManager>.instance.m_segments.m_buffer[SelectedSegmentId],
                    SelectedColor, SelectedColor);

            // Individual hovered lane (green bezier)
            if (HoveredLaneId_Static != 0)
                DrawLaneOverlay(cameraInfo, HoveredLaneId_Static);
        }

        private static void DrawLaneOverlay(RenderManager.CameraInfo cameraInfo, uint laneId)
        {
            try
            {
                Bezier3 bezier = Singleton<NetManager>.instance.m_lanes.m_buffer[laneId].m_bezier;
                Singleton<RenderManager>.instance.OverlayEffect.DrawBezier(
                    cameraInfo,
                    LaneColor,
                    bezier,
                    1.5f,
                    -1f,
                    -1f,
                    bezier.a.y - 2f,
                    bezier.d.y + 2f);
            }
            catch { /* laneId may be stale between frames */ }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            HoveredSegmentId    = 0;
            HoveredLaneId_Static = 0;
            // Hide panel whenever another tool is selected.
            LaneShiftPanel.Instance?.Hide();
            SelectedSegmentId = 0;
        }

        protected override void OnToolGUI(Event e) { }

        // ---- Raycasting ----
        private static ushort GetHoveredSegment()
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            var input = new RaycastInput(ray, Camera.main.farClipPlane)
            {
                m_ignoreTerrain      = true,
                m_ignoreSegmentFlags = NetSegment.Flags.None,
                m_ignoreNodeFlags    = NetNode.Flags.All
            };
            return RayCast(input, out RaycastOutput output) ? output.m_netSegment : (ushort)0;
        }

        public override NetNode.Flags GetNodeIgnoreFlags() => NetNode.Flags.All;
        public override NetSegment.Flags GetSegmentIgnoreFlags(out bool nameOnly)
        {
            nameOnly = false;
            return NetSegment.Flags.None;
        }
    }
}
