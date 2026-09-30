using ColossalFramework;
using ColossalFramework.UI;
using UnityEngine;

namespace JonShift
{
    /// <summary>
    /// Custom tool: hover + click a road segment to open the shift panel.
    /// Activate with the toolbar button or Alt+S hotkey.
    /// </summary>
    public class LaneShiftTool : DefaultTool
    {
        public static LaneShiftTool Instance { get; private set; }

        public ushort HoveredSegmentId { get; private set; }
        public ushort SelectedSegmentId { get; private set; }

        private static readonly Color32 HoverColor    = new Color32(0, 181, 255, 200);
        private static readonly Color32 SelectedColor = new Color32(255, 200, 0,  200);

        // ---- Lifecycle ----
        public static void Create()
        {
            if (Instance != null) return;
            ToolController controller = FindObjectOfType<ToolController>();
            Instance = controller.gameObject.AddComponent<LaneShiftTool>();
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

        // ---- Activation ----
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

        // ---- ToolBase overrides ----
        protected override void OnToolUpdate()
        {
            base.OnToolUpdate();

            // Alt+S to deactivate
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                DisableTool();
                return;
            }

            // Raycast for segment
            HoveredSegmentId = GetHoveredSegment();

            // Left-click selects
            if (Input.GetMouseButtonDown(0) && !UIView.IsInsideUI())
            {
                if (HoveredSegmentId != 0)
                {
                    SelectedSegmentId = HoveredSegmentId;
                    LaneShiftPanel.Instance?.ShowForSegment(SelectedSegmentId);
                }
            }

            // Show tooltip
            if (HoveredSegmentId != 0)
                ShowToolInfo(true, $"Segment #{HoveredSegmentId}\nClick to edit lane shifts", Vector3.zero);
            else
                ShowToolInfo(false, null, Vector3.zero);
        }

        public override void RenderOverlay(RenderManager.CameraInfo cameraInfo)
        {
            if (HoveredSegmentId != 0 && HoveredSegmentId != SelectedSegmentId)
                RenderSegmentOverlay(cameraInfo, HoveredSegmentId, HoverColor);

            if (SelectedSegmentId != 0)
                RenderSegmentOverlay(cameraInfo, SelectedSegmentId, SelectedColor);
        }

        private static void RenderSegmentOverlay(RenderManager.CameraInfo cameraInfo, ushort segId, Color color)
        {
            NetManager nm = Singleton<NetManager>.instance;
            ref NetSegment seg = ref nm.m_segments.m_buffer[segId];
            NetTool.RenderOverlay(cameraInfo, ref seg, color, color);
        }

        protected override void OnToolGUI(Event e) { /* no extra GUI */ }

        protected override void OnDisable()
        {
            base.OnDisable();
            HoveredSegmentId  = 0;
            // Keep SelectedSegmentId so panel stays open when user clicks UI
        }

        // ---- Raycasting ----
        private static ushort GetHoveredSegment()
        {
            Ray mouseRay = Camera.main.ScreenPointToRay(Input.mousePosition);
            var input = new RaycastInput(mouseRay, Camera.main.farClipPlane)
            {
                m_ignoreTerrain       = true,
                m_ignoreSegmentFlags  = NetSegment.Flags.None,
                m_ignoreNodeFlags     = NetNode.Flags.All
            };
            if (RayCast(input, out RaycastOutput output))
                return output.m_netSegment;
            return 0;
        }

        public override NetNode.Flags GetNodeIgnoreFlags()    => NetNode.Flags.All;
        public override NetSegment.Flags GetSegmentIgnoreFlags(out bool nameOnly) { nameOnly = false; return NetSegment.Flags.None; }
    }
}
