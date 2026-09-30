
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ColossalFramework;
using ColossalFramework.Math;
using ColossalFramework.UI;
using ICities;
using UnityEngine;

namespace LaneShift
{
    // ------------------------------------------------------------------ mod entry point
    public class LaneShiftUserMod : IUserMod
    {
        public string Name { get { return "Lane Shift"; } }
        public string Description
        {
            get { return "Shift individual lanes of a road segment sideways. Ctrl+L or the on-screen button."; }
        }
    }

    // ------------------------------------------------------------------ data
    public class SegmentRecord
    {
        public ushort Start;
        public ushort End;
        public string InfoName;
        public float[] Shift;          // one value per lane index of the segment's NetInfo
        public Bezier3[] Applied;      // last bezier we produced per lane (to detect game resets)
        public bool[] HasApplied;
        public bool NeedsReset;        // recompute the lanes from scratch, then re-apply shifts

        public SegmentRecord(ushort start, ushort end, string infoName, int laneCount)
        {
            Start = start;
            End = end;
            InfoName = infoName;
            Shift = new float[laneCount];
            Applied = new Bezier3[laneCount];
            HasApplied = new bool[laneCount];
            NeedsReset = true;
        }

        public bool AllZero()
        {
            if (Shift == null) return true;
            for (int i = 0; i < Shift.Length; i++)
            {
                if (Mathf.Abs(Shift[i]) >= 0.001f) return false;
            }
            return true;
        }
    }

    public static class LaneShiftData
    {
        // Using ConcurrentDictionary bypasses the need for lock() statements entirely, clearing the error!
        private static readonly ConcurrentDictionary<ushort, SegmentRecord> records = new ConcurrentDictionary<ushort, SegmentRecord>();

        public static void Clear()
        {
            records.Clear();
        }

        static bool IsValid(ushort id, SegmentRecord r)
        {
            if (r == null) return false;
            NetManager nm = Singleton<NetManager>.instance;
            if (id == 0 || id >= nm.m_segments.m_buffer.Length) return false;
            NetSegment seg = nm.m_segments.m_buffer[id];
            if ((seg.m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None) return false;
            NetInfo info = seg.Info;
            if (info == null) return false;
            return seg.m_startNode == r.Start
                && seg.m_endNode == r.End
                && info.name == r.InfoName
                && info.m_lanes.Length == r.Shift.Length;
        }

        public static float GetShift(ushort segmentId, int laneIndex)
        {
            SegmentRecord r;
            if (records.TryGetValue(segmentId, out r) && IsValid(segmentId, r)
                && laneIndex >= 0 && laneIndex < r.Shift.Length)
            {
                return r.Shift[laneIndex];
            }
            return 0f;
        }

        public static void SetShift(ushort segmentId, int laneIndex, float value)
        {
            NetManager nm = Singleton<NetManager>.instance;
            NetSegment seg = nm.m_segments.m_buffer[segmentId];
            NetInfo info = seg.Info;
            if (info == null || laneIndex < 0 || laneIndex >= info.m_lanes.Length) return;

            SegmentRecord r = records.GetOrAdd(segmentId, id => 
                new SegmentRecord(seg.m_startNode, seg.m_endNode, info.name, info.m_lanes.Length));

            if (!IsValid(segmentId, r))
            {
                r = new SegmentRecord(seg.m_startNode, seg.m_endNode, info.name, info.m_lanes.Length);
                records[segmentId] = r;
            }

            r.Shift[laneIndex] = value;
            r.NeedsReset = true;
            QueueTick();
        }

        public static void ResetSegment(ushort segmentId)
        {
            SegmentRecord r;
            if (records.TryGetValue(segmentId, out r))
            {
                for (int i = 0; i < r.Shift.Length; i++) r.Shift[i] = 0f;
                r.NeedsReset = true;
            }
            QueueTick();
        }

        static void QueueTick()
        {
            Singleton<SimulationManager>.instance.AddAction(delegate () { Tick(); });
        }

        public static void Tick()
        {
            if (records.IsEmpty) return;
            NetManager nm = Singleton<NetManager>.instance;
            List<ushort> remove = null;

            foreach (KeyValuePair<ushort, SegmentRecord> kv in records)
            {
                ushort id = kv.Key;
                SegmentRecord r = kv.Value;

                if (!IsValid(id, r))
                {
                    if (remove == null) remove = new List<ushort>();
                    remove.Add(id);
                    continue;
                }

                if (r.NeedsReset)
                {
                    nm.m_segments.m_buffer[id].UpdateLanes(id, true);
                    r.NeedsReset = false;
                    for (int i = 0; i < r.HasApplied.Length; i++) r.HasApplied[i] = false;
                }

                ApplySegment(nm, id, r);

                if (r.AllZero())
                {
                    if (remove == null) remove = new List<ushort>();
                    remove.Add(id);
                }
            }

            if (remove != null)
            {
                for (int i = 0; i < remove.Count; i++)
                {
                    SegmentRecord trash;
                    records.TryRemove(remove[i], out trash);
                }
            }
        }

        static void ApplySegment(NetManager nm, ushort id, SegmentRecord r)
        {
            NetInfo info = nm.m_segments.m_buffer[id].Info;
            uint laneId = nm.m_segments.m_buffer[id].m_lanes;
            bool changed = false;

            for (int i = 0; i < info.m_lanes.Length && laneId != 0; i++)
            {
                float s = r.Shift[i];
                if (Mathf.Abs(s) < 0.001f)
                {
                    r.HasApplied[i] = false;
                }
                else
                {
                    Bezier3 cur = nm.m_lanes.m_buffer[laneId].m_bezier;
                    if (!(r.HasApplied[i] && Same(r.Applied[i], cur)))
                    {
                        Bezier3 shifted = ShiftBezier(cur, s);
                        nm.m_lanes.m_buffer[laneId].m_bezier = shifted;
                        nm.m_lanes.m_buffer[laneId].UpdateLength();
                        r.Applied[i] = shifted;
                        r.HasApplied[i] = true;
                        changed = true;
                    }
                }
                laneId = nm.m_lanes.m_buffer[laneId].m_nextLane;
            }

            if (changed)
            {
                float total = 0f;
                int count = 0;
                uint l = nm.m_segments.m_buffer[id].m_lanes;
                for (int i = 0; i < info.m_lanes.Length && l != 0; i++)
                {
                    total += nm.m_lanes.m_buffer[l].m_length;
                    count++;
                    l = nm.m_lanes.m_buffer[l].m_nextLane;
                }
                if (count > 0) nm.m_segments.m_buffer[id].m_averageLength = total / count;
            }
        }

        static bool Same(Bezier3 a, Bezier3 b)
        {
            return a.a == b.a && a.b == b.b && a.c == b.c && a.d == b.d;
        }

        static Bezier3 ShiftBezier(Bezier3 bz, float shift)
        {
            float len0 = (bz.d - bz.a).magnitude;
            Vector3 dira = bz.b - bz.a;
            bz.a = Offset(bz.a, dira, shift);

            Vector3 dird = bz.c - bz.d;
            bz.d = Offset(bz.d, -dird, shift);

            float len = (bz.d - bz.a).magnitude;
            float ratio = len0 > 0.001f ? len / len0 : 1f;
            bz.b = bz.a + dira * ratio;
            bz.c = bz.d + dird * ratio;
            return bz;
        }

        static Vector3 Offset(Vector3 pos, Vector3 dir, float shift)
        {
            Vector3 right = new Vector3(dir.z, 0f, -dir.x);
            if (right.sqrMagnitude < 0.000001f) return pos;
            right.Normalize();
            return pos + right * shift;
        }

        public static byte[] Serialize()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("v1\n");
            int written = 0;
            foreach (KeyValuePair<ushort, SegmentRecord> kv in records)
            {
                SegmentRecord r = kv.Value;
                if (r == null || r.AllZero()) continue;
                sb.Append(kv.Key.ToString(CultureInfo.InvariantCulture)).Append('\t');
                sb.Append(r.Start.ToString(CultureInfo.InvariantCulture)).Append('\t');
                sb.Append(r.End.ToString(CultureInfo.InvariantCulture)).Append('\t');
                sb.Append(r.InfoName).Append('\t');
                for (int i = 0; i < r.Shift.Length; i++)
                {
                    if (i > 0) sb.Append(';');
                    sb.Append(r.Shift[i].ToString("R", CultureInfo.InvariantCulture));
                }
                sb.Append('\n');
                written++;
            }
            if (written == 0) return null;
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public static void Deserialize(byte[] data)
        {
            records.Clear();
            if (data == null) return;
            string text = Encoding.UTF8.GetString(data);
            string[] lines = text.Split('\n');
            for (int n = 1; n < lines.Length; n++)
            {
                string line = lines[n];
                if (line.Length == 0) continue;
                try
                {


string[] p = line.Split('\t');
ushort id = ushort.Parse(p[0], CultureInfo.InvariantCulture);
ushort start = ushort.Parse(p[1], CultureInfo.InvariantCulture);
ushort end = ushort.Parse(p[2], CultureInfo.InvariantCulture);
string[] vals = p[4].Split(';');
SegmentRecord r = new SegmentRecord(start, end, p[3], vals.Length);
for (int i = 0; i < vals.Length; i++)
{
r.Shift[i] = float.Parse(vals[i], NumberStyles.Float, CultureInfo.InvariantCulture);
}
r.NeedsReset = true;
records[id] = r;
}
catch (Exception ex)
{
Debug.LogWarning("[LaneShift] skipped bad save line: " + ex.Message);
}
}
}
}
// ------------------------------------------------------------------ UI helpers
public static class LaneShiftUI
{
public static UIButton MakeButton(UIComponent parent, string text, float w, float h, Vector3 pos)
{
UIButton b = (UIButton)parent.AddUIComponent(typeof(UIButton));
b.text = text;
b.width = w;
b.height = h;
b.relativePosition = pos;
b.normalBgSprite = "ButtonMenu";
b.hoveredBgSprite = "ButtonMenuHovered";
b.pressedBgSprite = "ButtonMenuPressed";
b.disabledBgSprite = "ButtonMenuDisabled";
b.textColor = new Color32(255, 255, 255, 255);
b.textScale = 0.9f;
b.textHorizontalAlignment = UIHorizontalAlignment.Center;
b.textVerticalAlignment = UIVerticalAlignment.Middle;
return b;
}
public static UILabel MakeLabel(UIComponent parent, string text, float w, Vector3 pos)
{
UILabel l = (UILabel)parent.AddUIComponent(typeof(UILabel));
l.autoSize = false;
l.width = w;
l.height = 22f;
l.text = text;
l.textScale = 0.85f;
l.relativePosition = pos;
l.isInteractive = false;
return l;
}
public static UITextField MakeField(UIComponent parent, float w, float h, Vector3 pos)
{
UITextField f = (UITextField)parent.AddUIComponent(typeof(UITextField));
f.width = w;
f.height = h;
f.relativePosition = pos;
f.normalBgSprite = "TextFieldPanel";
f.hoveredBgSprite = "TextFieldPanelHovered";
f.focusedBgSprite = "TextFieldPanel";
f.selectionSprite = "EmptySprite";
f.color = new Color32(70, 80, 95, 255);
f.textColor = new Color32(255, 255, 255, 255);
f.textScale = 0.9f;
f.padding = new RectOffset(6, 6, 6, 4);
f.builtinKeyNavigation = true;
f.isInteractive = true;
f.readOnly = false;
f.selectOnFocus = true;
f.submitOnFocusLost = true;
return f;
}
}
public static class LaneShiftPanel
{
const float Width = 380f;
const float Step = 0.1f;
static UIPanel panel;
static UILabel titleLabel;
static List rows = new List();
static ushort currentSegment;
public static void Create()
{
UIView view = UIView.GetAView();
panel = (UIPanel)view.AddUIComponent(typeof(UIPanel));
panel.name = "LaneShiftPanel";
panel.backgroundSprite = "MenuPanel2";
panel.width = Width;
panel.height = 120f;
Vector2 res = view.GetScreenResolution();
panel.relativePosition = new Vector3(res.x - Width - 20f, 120f);
panel.isVisible = false;
UIDragHandle drag = (UIDragHandle)panel.AddUIComponent(typeof(UIDragHandle));
drag.width = Width;
drag.height = 36f;
drag.relativePosition = Vector3.zero;
drag.target = panel;
titleLabel = LaneShiftUI.MakeLabel(panel, "Lane Shift", Width - 60f, new Vector3(14f, 10f));
UIButton close = LaneShiftUI.MakeButton(panel, "X", 28f, 24f, new Vector3(Width - 38f, 6f));
close.eventClick += delegate (UIComponent c, UIMouseEventParameter p) { LaneShiftTool.Deactivate(); };
}
public static void Destroy()
{
if (panel != null)
{
UnityEngine.Object.Destroy(panel.gameObject);
panel = null;
}
rows.Clear();
}
public static void Hide()
{
if (panel != null) panel.isVisible = false;
}
static void ClearRows()
{
for (int i = 0; i < rows.Count; i++)
{
if (rows[i] != null)
{
panel.RemoveUIComponent(rows[i]);
UnityEngine.Object.Destroy(rows[i].gameObject);
}
}
rows.Clear();
}
static string Format(float v)
{
return v.ToString("0.##", CultureInfo.InvariantCulture);
}
static float Parse(string s, float fallback)
{
float v;
s = s.Trim().Replace(',', '.');
if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
return fallback;
}
static void SetLane(int laneIndex, UITextField field, float value)
{
value = Mathf.Clamp(value, -20f, 20f);
value = (float)Math.Round(value, 2);
LaneShiftData.SetShift(currentSegment, laneIndex, value);
field.text = Format(value);
}
public static void Show(ushort segmentId)
{
if (panel == null) return;
ClearRows();
currentSegment = segmentId;
NetInfo info = Singleton.instance.m_segments.m_buffer[segmentId].Info;
if (info == null)
{
panel.isVisible = false;
return;
}
string name = info.name;
if (name.Length > 28) name = name.Substring(0, 28) + "...";
titleLabel.text = "Segment " + segmentId + " - " + name;
float y = 44f;
for (int i = 0; i < info.m_lanes.Length; i++)
{
NetInfo.Lane lane = info.m_lanes[i];
int laneIndex = i;
string caption = "#" + i + " " + lane.m_laneType.ToString() + " ("
+ lane.m_position.ToString("0.0", CultureInfo.InvariantCulture) + ")";
rows.Add(LaneShiftUI.MakeLabel(panel, caption, 170f, new Vector3(14f, y + 4f)));
UIButton minus = LaneShiftUI.MakeButton(panel, "-", 26f, 26f, new Vector3(190f, y));
UITextField field = LaneShiftUI.MakeField(panel, 70f, 26f, new Vector3(220f, y));
UIButton plus = LaneShiftUI.MakeButton(panel, "+", 26f, 26f, new Vector3(296f, y));
rows.Add(minus);
rows.Add(field);
rows.Add(plus);
field.text = Format(LaneShiftData.GetShift(segmentId, laneIndex));
minus.eventClick += delegate (UIComponent c, UIMouseEventParameter p)
{
float cur = Parse(field.text, LaneShiftData.GetShift(currentSegment, laneIndex));
SetLane(laneIndex, field, cur - Step);
};
plus.eventClick += delegate (UIComponent c, UIMouseEventParameter p)
{
float cur = Parse(field.text, LaneShiftData.GetShift(currentSegment, laneIndex));
SetLane(laneIndex, field, cur + Step);
};
field.eventTextSubmitted += delegate (UIComponent c, string value)
{
SetLane(laneIndex, field, Parse(value, LaneShiftData.GetShift(currentSegment, laneIndex)));
};
y += 32f;
}
UIButton reset = LaneShiftUI.MakeButton(panel, "Reset all lanes", 140f, 28f, new Vector3(14f, y + 6f));
reset.eventClick += delegate (UIComponent c, UIMouseEventParameter p)
{
LaneShiftData.ResetSegment(currentSegment);
Show(currentSegment);
};
rows.Add(reset);
panel.height = y + 48f;
panel.isVisible = true;
}
}
public static class LaneShiftButton
{
static UIButton button;
public static void Create()
{
UIView view = UIView.GetAView();
button = (UIButton)view.gameObject.AddComponent(typeof(UIButton));
button.text = "Lane Shift";
button.width = 90f;
button.height = 28f;
button.relativePosition = new Vector3(10f, 90f);
button.normalBgSprite = "ButtonMenu";
button.hoveredBgSprite = "ButtonMenuHovered";
button.pressedBgSprite = "ButtonMenuPressed";
button.disabledBgSprite = "ButtonMenuDisabled";
button.textColor = new Color32(255, 255, 255, 255);
button.textScale = 0.9f;
button.textHorizontalAlignment = UIHorizontalAlignment.Center;
button.textVerticalAlignment = UIVerticalAlignment.Middle;
button.name = "LaneShiftButton";
button.eventClick += delegate (UIComponent c, UIMouseEventParameter p) { LaneShiftTool.Toggle(); };
}
public static void Destroy()
{
if (button != null)
{
UnityEngine.Object.Destroy(button.gameObject);
button = null;
}
}
}
public class LaneShiftKeys : MonoBehaviour
{
void Update()
{
if (UIView.HasInputFocus()) return;
bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
if (ctrl && Input.GetKeyDown(KeyCode.L)) LaneShiftTool.Toggle();
}
}
// ------------------------------------------------------------------ the tool
public class LaneShiftTool : ToolBase
{
public static LaneShiftTool Instance;
ushort hovered;
ushort selected;
public static void Create()
{
GameObject go = ToolsModifierControl.toolController.gameObject;
Instance = go.AddComponent();
}
public static void Remove()
{
if (Instance != null)
{
Deactivate();
UnityEngine.Object.Destroy(Instance);
Instance = null;
}
}
public static void Toggle()
{
if (Instance == null) return;
if (ToolsModifierControl.toolController.CurrentTool == Instance) Deactivate();
else ToolsModifierControl.toolController.CurrentTool = Instance;
}
public static void Deactivate()
{
if (Instance != null && ToolsModifierControl.toolController.CurrentTool == Instance)
{
ToolsModifierControl.SetTool();
}
}
protected override void Awake()
{
base.Awake();
enabled = false;
}
protected override void OnEnable()
{
base.OnEnable();
hovered = 0;
selected = 0;
LaneShiftPanel.Hide();
}
protected override void OnDisable()
{
base.OnDisable();
hovered = 0;
selected = 0;
LaneShiftPanel.Hide();
}
protected override void OnToolGUI(Event e)
{
if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
{
e.Use();
Deactivate();
}
}
protected override void OnToolUpdate()
{
base.OnToolUpdate();
try
{
hovered = 0;
bool valid = !UIView.IsInsideUI() && Cursor.visible;
if (!valid) return;
Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
float length = Camera.main.farClipPlane;
RaycastInput input = new RaycastInput(ray, length);
input.m_netService = new RaycastService(ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Layer.Default);
input.m_ignoreSegmentFlags = NetSegment.Flags.None;
input.m_ignoreTerrain = true;
RaycastOutput output;
if (RayCast(input, out output))
{
hovered = output.m_netSegment;
}
if (hovered != 0 && Input.GetMouseButtonUp(0))
{
selected = hovered;
LaneShiftPanel.Show(selected);
}
}
catch (Exception ex)
{
Debug.LogException(ex);
}
}
public override void RenderOverlay(RenderManager.CameraInfo cameraInfo)
{
base.RenderOverlay(cameraInfo);
try
{
if (hovered != 0 && hovered != selected)
RenderSegment(cameraInfo, hovered, new Color(0.2f, 0.5f, 1f, 0.6f));
if (selected != 0)
{
RenderSegment(cameraInfo, selected, new Color(0.2f, 1f, 0.4f, 0.35f));
RenderLanes(cameraInfo, selected);
}
}
catch (Exception ex)
{
Debug.LogException(ex);
}
}
static void RenderSegment(RenderManager.CameraInfo cameraInfo, ushort id, Color color)
{
NetManager nm = Singleton.instance;
NetSegment seg = nm.m_segments.m_buffer[id];
if ((seg.m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None || seg.Info == null) return;
Bezier3 bz;
bz.a = nm.m_nodes.m_buffer[seg.m_startNode].m_position;
bz.d = nm.m_nodes.m_buffer[seg.m_endNode].m_position;
bool startMiddle = (nm.m_nodes.m_buffer[seg.m_startNode].m_flags & NetNode.Flags.Middle) != NetNode.Flags.None;
bool endMiddle = (nm.m_nodes.m_buffer[seg.m_endNode].m_flags & NetNode.Flags.Middle) != NetNode.Flags.None;
NetSegment.CalculateMiddlePoints(bz.a, seg.m_startDirection, bz.d, seg.m_endDirection,
startMiddle, endMiddle, out bz.b, out bz.c);
float hw = seg.Info.m_halfWidth;
Singleton.instance.m_drawCallData.m_overlayCalls++;
RenderManager.instance.OverlayEffect.DrawBezier(cameraInfo, color, bz, hw * 2f, hw, hw, -1f, 1024f, false, true);
}
static void RenderLanes(RenderManager.CameraInfo cameraInfo, ushort id)
{
NetManager nm = Singleton.instance;
NetInfo info = nm.m_segments.m_buffer[id].Info;
if (info == null) return;
uint laneId = nm.m_segments.m_buffer[id].m_lanes;
for (int i = 0; i < info.m_lanes.Length && laneId != 0; i++)
{
float width = Mathf.Max(0.6f, info.m_lanes[i].m_width * 0.5f);
Singleton.instance.m_drawCallData.m_overlayCalls++;
RenderManager.instance.OverlayEffect.DrawBezier(cameraInfo, new Color(1f, 0.9f, 0.2f, 0.9f),
nm.m_lanes.m_buffer[laneId].m_bezier, width, 0f, 0f, -1f, 1024f, false, true);
laneId = nm.m_lanes.m_buffer[laneId].m_nextLane;
}
}
}
// ------------------------------------------------------------------ lifecycle
public class LaneShiftLoading : LoadingExtensionBase
{
static GameObject keysObject;
public override void OnLevelLoaded(LoadMode mode)
{
if (mode != LoadMode.NewGame && mode != LoadMode.LoadGame && mode != LoadMode.NewGameFromScenario) return;
try
{
LaneShiftTool.Create();
LaneShiftPanel.Create();
LaneShiftButton.Create();
keysObject = new GameObject("LaneShiftKeys");
keysObject.AddComponent();
}
catch (Exception ex)
{
Debug.LogException(ex);
}
}
public override void OnLevelUnloading()
{
try
{
LaneShiftTool.Remove();
LaneShiftPanel.Destroy();
LaneShiftButton.Destroy();
if (keysObject != null)
{
UnityEngine.Object.Destroy(keysObject);
keysObject = null;
}
LaneShiftData.Clear();
}
catch (Exception ex)
{
Debug.LogException(ex);
}
}
}
public class LaneShiftThreading : ThreadingExtensionBase
{
public override void OnAfterSimulationTick()
{
try { LaneShiftData.Tick(); }
catch (Exception ex) { Debug.LogException(ex); }
}
}
public class LaneShiftSerialization : SerializableDataExtensionBase
{
const string Key = "LaneShift_v1";
public override void OnLoadData()
{
try { LaneShiftData.Deserialize(serializableDataManager.LoadData(Key)); }
catch (Exception ex) { Debug.LogException(ex); }
}
public override void OnSaveData()
{
try
{
byte[] data = LaneShiftData.Serialize();
if (data != null) serializableDataManager.SaveData(Key, data);
}
catch (Exception ex) { Debug.LogException(ex); }
}
}
}
