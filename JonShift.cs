using System;
using System.Collections.Generic;
using System.Reflection;
using System.Globalization;
using System.Text;
using CitiesHarmony.API;
using ColossalFramework;
using ColossalFramework.Math;
using ColossalFramework.UI;
using ICities;
using UnityEngine;

namespace LaneShift
{
    // =====================================================================
    // Mod entry point
    // =====================================================================
    public class LaneShiftUserMod : IUserMod
    {
        public string Name { get { return "Lane Shift"; } }

        public string Description
        {
            get { return "Shift individual pedestrian, vehicle, transit, and other lanes on placed network segments."; }
        }

        // Deliberately no Harmony types are referenced from IUserMod itself.
        // The Harmony API is handled by LaneShiftPatcher, as recommended by
        // CitiesHarmony.
        public void OnEnabled()
        {
            LaneShiftPatcher.Enable();
        }

        public void OnDisabled()
        {
            LaneShiftPatcher.Disable();
        }
    }

    // =====================================================================
    // Harmony bootstrap / patch
    // =====================================================================
    public static class LaneShiftPatcher
    {
        private const string HarmonyId = "JonathanMonaghan.LaneShift";
        private static bool enabled;
        private static bool patched;

        public static void Enable()
        {
            enabled = true;

            // CitiesHarmony can auto-install/prepare itself and then invokes
            // our patcher when Harmony is ready.
            HarmonyHelper.DoOnHarmonyReady(delegate
            {
                if (enabled)
                {
                    PatchAll();
                }
            });
        }

        public static void Disable()
        {
            enabled = false;

            if (HarmonyHelper.IsHarmonyInstalled)
            {
                UnpatchAll();
            }
        }

        private static void PatchAll()
        {
            if (patched) return;

            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(HarmonyId);

            MethodInfo original = typeof(NetSegment).GetMethod(
                "UpdateLanes",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(ushort), typeof(bool) },
                null);

            MethodInfo postfix = typeof(LaneShiftPatcher).GetMethod(
                "UpdateLanesPostfix",
                BindingFlags.Static | BindingFlags.NonPublic);

            if (original == null || postfix == null)
            {
                Debug.LogError("[LaneShift] Could not find NetSegment.UpdateLanes; lane shifting is disabled.");
                return;
            }

            harmony.Patch(
                original,
                postfix: new HarmonyLib.HarmonyMethod(postfix));

            patched = true;
            Debug.Log("[LaneShift] Harmony patch installed.");
        }

        private static void UpdateLanesPostfix(ushort segmentID)
        {
            try
            {
                LaneShiftData.Apply(segmentID);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private static void UnpatchAll()
        {
            if (!patched) return;

            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(HarmonyId);
            harmony.UnpatchAll(HarmonyId);
            patched = false;
            Debug.Log("[LaneShift] Harmony patch removed.");
        }
    }

    // =====================================================================
    // Saved per-segment data
    // =====================================================================
    public class SegmentRecord
    {
        public ushort Start;
        public ushort End;
        public string InfoName;
        public float[] Shift;

        // UI runs on Unity's main thread while UpdateLanes runs from game
        // code/simulation. This flag is protected by LaneShiftData.Sync.
        public bool UpdateQueued;

        public SegmentRecord(ushort start, ushort end, string infoName, int laneCount)
        {
            Start = start;
            End = end;
            InfoName = infoName ?? string.Empty;
            Shift = new float[Mathf.Max(0, laneCount)];
            UpdateQueued = false;
        }

        public bool AllZero()
        {
            if (Shift == null) return true;

            for (int i = 0; i < Shift.Length; i++)
            {
                if (Mathf.Abs(Shift[i]) >= 0.001f)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public static class LaneShiftData
    {
        public static readonly object Sync = new object();

        private static readonly Dictionary<ushort, SegmentRecord> records =
            new Dictionary<ushort, SegmentRecord>();

        public static void Clear()
        {
            lock (Sync)
            {
                records.Clear();
            }
        }

        // Called by the UI after it has read the current segment information.
        public static void EnsureSegment(
            ushort segmentId,
            ushort startNode,
            ushort endNode,
            string infoName,
            int laneCount)
        {
            lock (Sync)
            {
                SegmentRecord record;
                if (!records.TryGetValue(segmentId, out record) ||
                    record == null ||
                    record.Start != startNode ||
                    record.End != endNode ||
                    !string.Equals(record.InfoName, infoName, StringComparison.Ordinal) ||
                    record.Shift == null ||
                    record.Shift.Length != laneCount)
                {
                    records[segmentId] = new SegmentRecord(startNode, endNode, infoName, laneCount);
                }
            }
        }

        public static float GetShift(ushort segmentId, int laneIndex)
        {
            lock (Sync)
            {
                SegmentRecord record;
                if (records.TryGetValue(segmentId, out record) &&
                    record != null &&
                    record.Shift != null &&
                    laneIndex >= 0 &&
                    laneIndex < record.Shift.Length)
                {
                    return record.Shift[laneIndex];
                }

                return 0f;
            }
        }

        // UI-safe: this method does NOT touch NetManager. It only updates our
        // data and queues a simulation action that asks CS1 to rebuild the
        // segment. The Harmony postfix then applies the offsets.
        public static void SetShift(ushort segmentId, int laneIndex, float value)
        {
            bool queueUpdate = false;

            lock (Sync)
            {
                SegmentRecord record;
                if (!records.TryGetValue(segmentId, out record) ||
                    record == null ||
                    record.Shift == null ||
                    laneIndex < 0 ||
                    laneIndex >= record.Shift.Length)
                {
                    return;
                }

                record.Shift[laneIndex] = value;

                if (!record.UpdateQueued)
                {
                    record.UpdateQueued = true;
                    queueUpdate = true;
                }
            }

            if (queueUpdate)
            {
                QueueSegmentUpdate(segmentId);
            }
        }

        public static void ResetSegment(ushort segmentId)
        {
            bool queueUpdate = false;

            lock (Sync)
            {
                SegmentRecord record;
                if (!records.TryGetValue(segmentId, out record) || record == null)
                {
                    return;
                }

                if (record.Shift != null)
                {
                    for (int i = 0; i < record.Shift.Length; i++)
                    {
                        record.Shift[i] = 0f;
                    }
                }

                if (!record.UpdateQueued)
                {
                    record.UpdateQueued = true;
                    queueUpdate = true;
                }
            }

            if (queueUpdate)
            {
                QueueSegmentUpdate(segmentId);
            }
        }

        private static void QueueSegmentUpdate(ushort segmentId)
        {
            try
            {
                SimulationManager simulation = Singleton<SimulationManager>.instance;
                if (simulation == null)
                {
                    return;
                }

                simulation.AddAction(delegate
                {
                    RebuildSegment(segmentId);
                });
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private static void RebuildSegment(ushort segmentId)
        {
            NetManager netManager = Singleton<NetManager>.instance;

            lock (Sync)
            {
                SegmentRecord record;
                if (records.TryGetValue(segmentId, out record) && record != null)
                {
                    record.UpdateQueued = false;
                }
            }

            if (netManager == null || segmentId == 0 || segmentId >= netManager.m_segments.m_buffer.Length)
            {
                return;
            }

            NetSegment segment = netManager.m_segments.m_buffer[segmentId];
            if ((segment.m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None)
            {
                Remove(segmentId);
                return;
            }

            // The original game rebuilds lane geometry here. Our Harmony
            // postfix immediately reapplies the stored offsets from fresh
            // geometry. Call the array element directly because NetSegment
            // is a struct and UpdateLanes mutates the stored segment.
            netManager.m_segments.m_buffer[segmentId].UpdateLanes(segmentId, true);
        }

        private static void Remove(ushort segmentId)
        {
            lock (Sync)
            {
                records.Remove(segmentId);
            }
        }

        private static bool IsValid(
            ushort segmentId,
            SegmentRecord record,
            NetManager netManager,
            out NetSegment segment,
            out NetInfo info)
        {
            segment = default(NetSegment);
            info = null;

            if (record == null || netManager == null || segmentId == 0)
            {
                return false;
            }

            if (segmentId >= netManager.m_segments.m_buffer.Length)
            {
                return false;
            }

            segment = netManager.m_segments.m_buffer[segmentId];

            if ((segment.m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None)
            {
                return false;
            }

            info = segment.Info;
            if (info == null || info.m_lanes == null)
            {
                return false;
            }

            return segment.m_startNode == record.Start &&
                   segment.m_endNode == record.End &&
                   string.Equals(info.name, record.InfoName, StringComparison.Ordinal) &&
                   info.m_lanes.Length == record.Shift.Length;
        }

        // Called from the Harmony postfix, i.e. after stock lane geometry has
        // been rebuilt. This method is intentionally idempotent relative to a
        // normal UpdateLanes call: the source geometry is fresh every time.
        public static void Apply(ushort segmentId)
        {
            NetManager netManager = Singleton<NetManager>.instance;
            SegmentRecord record;
            float[] shifts;
            bool removeAfterApply = false;

            lock (Sync)
            {
                if (!records.TryGetValue(segmentId, out record) || record == null)
                {
                    return;
                }

                shifts = record.Shift != null ? (float[])record.Shift.Clone() : null;
            }

            NetSegment segment;
            NetInfo info;
            if (!IsValid(segmentId, record, netManager, out segment, out info))
            {
                Remove(segmentId);
                return;
            }

            if (shifts == null || shifts.Length == 0)
            {
                Remove(segmentId);
                return;
            }

            if (record == null)
            {
                return;
            }

            uint laneId = segment.m_lanes;
            bool changed = false;

            for (int laneIndex = 0;
                 laneIndex < info.m_lanes.Length &&
                 laneIndex < shifts.Length &&
                 laneId != 0;
                 laneIndex++)
            {
                float shift = shifts[laneIndex];

                if (Mathf.Abs(shift) < 0.001f)
                {
                    laneId = netManager.m_lanes.m_buffer[laneId].m_nextLane;
                    continue;
                }

                NetLane lane = netManager.m_lanes.m_buffer[laneId];
                lane.m_bezier = ShiftBezier(lane.m_bezier, shift);
                lane.UpdateLength();
                netManager.m_lanes.m_buffer[laneId] = lane;

                changed = true;
                laneId = lane.m_nextLane;
            }

            // Do not immediately delete a zero-valued record during a call
            // from ResetSegment until the stock UpdateLanes has completed;
            // this postfix is that completion point.
            lock (Sync)
            {
                SegmentRecord latest;
                if (records.TryGetValue(segmentId, out latest) && latest != null)
                {
                    removeAfterApply = latest.AllZero();
                }
            }

            if (changed)
            {
                // Update the aggregate segment length used by some stock
                // traffic calculations after we changed the lane curves.
                float total = 0f;
                int count = 0;
                uint currentLane = segment.m_lanes;

                for (int laneIndex = 0;
                     laneIndex < info.m_lanes.Length && currentLane != 0;
                     laneIndex++)
                {
                    total += netManager.m_lanes.m_buffer[currentLane].m_length;
                    count++;
                    currentLane = netManager.m_lanes.m_buffer[currentLane].m_nextLane;
                }

                if (count > 0)
                {
                    segment.m_averageLength = total / count;
                    netManager.m_segments.m_buffer[segmentId] = segment;
                }
            }

            if (removeAfterApply)
            {
                Remove(segmentId);
            }
        }

        public static void RebuildAllSavedSegments()
        {
            try
            {
                SimulationManager simulation = Singleton<SimulationManager>.instance;
                if (simulation == null) return;

                simulation.AddAction(delegate
                {
                    List<ushort> ids = new List<ushort>();

                    lock (Sync)
                    {
                        foreach (KeyValuePair<ushort, SegmentRecord> pair in records)
                        {
                            if (pair.Value != null && !pair.Value.AllZero())
                            {
                                ids.Add(pair.Key);
                            }
                        }
                    }

                    for (int i = 0; i < ids.Count; i++)
                    {
                        ushort segmentId = ids[i];
                        NetManager netManager = Singleton<NetManager>.instance;
                        if (netManager == null || segmentId == 0 || segmentId >= netManager.m_segments.m_buffer.Length)
                        {
                            continue;
                        }

                        NetSegment segment = netManager.m_segments.m_buffer[segmentId];
                        if ((segment.m_flags & NetSegment.Flags.Created) != NetSegment.Flags.None)
                        {
                            netManager.m_segments.m_buffer[segmentId].UpdateLanes(segmentId, true);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private static Bezier3 ShiftBezier(Bezier3 bezier, float shift)
        {
            // CS1 stores each lane as a cubic Bezier. We offset the two
            // control-point pairs using the horizontal tangent at each end.
            // This preserves the general curvature much better than simply
            // translating the entire curve by one world-space vector.
            Vector3 startDirection = bezier.b - bezier.a;
            Vector3 endDirection = bezier.d - bezier.c;

            Vector3 startRight = RightVector(startDirection);
            Vector3 endRight = RightVector(endDirection);

            bezier.a += startRight * shift;
            bezier.b += startRight * shift;
            bezier.c += endRight * shift;
            bezier.d += endRight * shift;

            return bezier;
        }

        private static Vector3 RightVector(Vector3 direction)
        {
            Vector3 horizontal = new Vector3(direction.x, 0f, direction.z);

            if (horizontal.sqrMagnitude < 0.000001f)
            {
                return Vector3.zero;
            }

            horizontal.Normalize();
            Vector3 right = new Vector3(horizontal.z, 0f, -horizontal.x);
            right.Normalize();
            return right;
        }

        // --------------------------------------------------------------- save
        public static byte[] Serialize()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("v1\n");

            lock (Sync)
            {
                foreach (KeyValuePair<ushort, SegmentRecord> pair in records)
                {
                    SegmentRecord record = pair.Value;
                    if (record == null || record.AllZero())
                    {
                        continue;
                    }

                    builder.Append(pair.Key.ToString(CultureInfo.InvariantCulture)).Append('\t');
                    builder.Append(record.Start.ToString(CultureInfo.InvariantCulture)).Append('\t');
                    builder.Append(record.End.ToString(CultureInfo.InvariantCulture)).Append('\t');
                    builder.Append(record.InfoName ?? string.Empty).Append('\t');

                    for (int i = 0; i < record.Shift.Length; i++)
                    {
                        if (i > 0) builder.Append(';');
                        builder.Append(record.Shift[i].ToString("R", CultureInfo.InvariantCulture));
                    }

                    builder.Append('\n');
                }
            }

            string text = builder.ToString();
            if (text == "v1\n")
            {
                return null;
            }

            return Encoding.UTF8.GetBytes(text);
        }

        public static void Deserialize(byte[] data)
        {
            lock (Sync)
            {
                records.Clear();
            }

            if (data == null || data.Length == 0)
            {
                return;
            }

            try
            {
                string text = Encoding.UTF8.GetString(data);
                string[] lines = text.Split('\n');

                for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
                {
                    string line = lines[lineIndex].TrimEnd('\r');
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    try
                    {
                        string[] parts = line.Split('\t');
                        if (parts.Length < 5)
                        {
                            continue;
                        }

                        ushort segmentId = ushort.Parse(parts[0], CultureInfo.InvariantCulture);
                        ushort start = ushort.Parse(parts[1], CultureInfo.InvariantCulture);
                        ushort end = ushort.Parse(parts[2], CultureInfo.InvariantCulture);
                        string infoName = parts[3];
                        string[] values = parts[4].Split(';');

                        SegmentRecord record = new SegmentRecord(start, end, infoName, values.Length);

                        for (int i = 0; i < values.Length; i++)
                        {
                            record.Shift[i] = float.Parse(
                                values[i],
                                NumberStyles.Float,
                                CultureInfo.InvariantCulture);
                        }

                        lock (Sync)
                        {
                            records[segmentId] = record;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[LaneShift] Skipping save line: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public static void PruneInvalidRecords()
        {
            NetManager netManager = Singleton<NetManager>.instance;
            if (netManager == null)
            {
                return;
            }

            List<ushort> removeIds = new List<ushort>();

            lock (Sync)
            {
                foreach (KeyValuePair<ushort, SegmentRecord> pair in records)
                {
                    NetSegment dummySegment;
                    NetInfo dummyInfo;

                    if (!IsValid(pair.Key, pair.Value, netManager, out dummySegment, out dummyInfo))
                    {
                        removeIds.Add(pair.Key);
                    }
                }

                for (int i = 0; i < removeIds.Count; i++)
                {
                    records.Remove(removeIds[i]);
                }
            }
        }
    }

    // =====================================================================
    // UI helpers
    // =====================================================================
    public static class LaneShiftUI
    {
        public static UIButton MakeButton(
            UIComponent parent,
            string text,
            float width,
            float height,
            Vector3 position)
        {
            UIButton button = parent.AddUIComponent<UIButton>();
            button.text = text;
            button.width = width;
            button.height = height;
            button.relativePosition = position;
            button.normalBgSprite = "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.pressedBgSprite = "ButtonMenuPressed";
            button.disabledBgSprite = "ButtonMenuDisabled";
            button.textColor = new Color32(255, 255, 255, 255);
            button.textScale = 0.9f;
            button.textHorizontalAlignment = UIHorizontalAlignment.Center;
            button.textVerticalAlignment = UIVerticalAlignment.Middle;
            return button;
        }

        public static UILabel MakeLabel(
            UIComponent parent,
            string text,
            float width,
            Vector3 position,
            float height = 22f)
        {
            UILabel label = parent.AddUIComponent<UILabel>();
            label.autoSize = false;
            label.width = width;
            label.height = height;
            label.text = text;
            label.textScale = 0.85f;
            label.relativePosition = position;
            label.isInteractive = false;
            return label;
        }

        public static UITextField MakeField(
            UIComponent parent,
            float width,
            float height,
            Vector3 position)
        {
            UITextField field = parent.AddUIComponent<UITextField>();
            field.width = width;
            field.height = height;
            field.relativePosition = position;
            field.normalBgSprite = "TextFieldPanel";
            field.hoveredBgSprite = "TextFieldPanelHovered";
            field.focusedBgSprite = "TextFieldPanel";
            field.selectionSprite = "EmptySprite";
            field.color = new Color32(70, 80, 95, 255);
            field.textColor = new Color32(255, 255, 255, 255);
            field.textScale = 0.9f;
            field.padding = new RectOffset(6, 6, 5, 4);
            field.builtinKeyNavigation = true;
            field.isInteractive = true;
            field.readOnly = false;
            field.selectOnFocus = true;
            field.submitOnFocusLost = true;
            return field;
        }
    }

    // =====================================================================
    // Lane Shift window
    // =====================================================================
    public static class LaneShiftPanel
    {
        private const float Width = 455f;
        private const float HeaderHeight = 62f;
        private const float RowHeight = 32f;
        private const float FooterHeight = 40f;
        private const float Step = 0.1f;
        private const float MaxShift = 20f;

        private static UIPanel panel;
        private static UILabel titleLabel;
        private static UILabel helpLabel;
        private static readonly List<UIComponent> rows = new List<UIComponent>();
        private static ushort currentSegment;

        public static void Create()
        {
            if (panel != null)
            {
                return;
            }

            UIView view = UIView.GetAView();
            if (view == null)
            {
                return;
            }

            panel = view.AddUIComponent<UIPanel>();
            panel.name = "LaneShiftPanel";
            panel.backgroundSprite = "MenuPanel2";
            panel.width = Width;
            panel.height = 150f;

            Vector2 resolution = view.GetScreenResolution();
            panel.relativePosition = new Vector3(
                Mathf.Max(10f, resolution.x - Width - 20f),
                110f);

            panel.isVisible = false;

            UIDragHandle drag = panel.AddUIComponent<UIDragHandle>();
            drag.width = Width;
            drag.height = 34f;
            drag.relativePosition = Vector3.zero;
            drag.target = panel;

            titleLabel = LaneShiftUI.MakeLabel(
                panel,
                "Lane Shift",
                Width - 70f,
                new Vector3(14f, 7f));
            titleLabel.textScale = 1.0f;

            helpLabel = LaneShiftUI.MakeLabel(
                panel,
                "Meters • positive = right from start node to end node",
                Width - 30f,
                new Vector3(14f, 31f));
            helpLabel.textScale = 0.72f;

            UIButton close = LaneShiftUI.MakeButton(
                panel,
                "X",
                28f,
                24f,
                new Vector3(Width - 38f, 5f));

            close.eventClick += delegate(UIComponent component, UIMouseEventParameter eventParam)
            {
                LaneShiftTool.Deactivate();
            };
        }

        public static void Destroy()
        {
            if (panel != null)
            {
                UnityEngine.Object.Destroy(panel.gameObject);
                panel = null;
            }

            titleLabel = null;
            helpLabel = null;
            rows.Clear();
            currentSegment = 0;
        }

        public static void Hide()
        {
            if (panel != null)
            {
                panel.isVisible = false;
            }
        }

        private static void ClearRows()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                UIComponent row = rows[i];
                if (row != null)
                {
                    panel.RemoveUIComponent(row);
                    UnityEngine.Object.Destroy(row.gameObject);
                }
            }

            rows.Clear();
        }

        private static string Format(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static float Parse(string text, float fallback)
        {
            if (string.IsNullOrEmpty(text))
            {
                return fallback;
            }

            float value;
            text = text.Trim().Replace(',', '.');

            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            return fallback;
        }

        private static void SetLane(int laneIndex, UITextField field, float value)
        {
            value = Mathf.Clamp(value, -MaxShift, MaxShift);
            value = (float)Math.Round(value, 2);
            LaneShiftData.SetShift(currentSegment, laneIndex, value);
            field.text = Format(value);
        }

        private static string LaneTypeName(NetInfo.Lane lane)
        {
            if ((lane.m_laneType & NetInfo.LaneType.Vehicle) != NetInfo.LaneType.None)
                return "Vehicle";
            if ((lane.m_laneType & NetInfo.LaneType.TransportVehicle) != NetInfo.LaneType.None)
                return "Transit";
            if ((lane.m_laneType & NetInfo.LaneType.Pedestrian) != NetInfo.LaneType.None)
                return "Pedestrian";
            return lane.m_laneType.ToString();
        }

        public static void Show(ushort segmentId)
        {
            if (panel == null)
            {
                return;
            }

            NetManager netManager = Singleton<NetManager>.instance;
            if (netManager == null || segmentId == 0 || segmentId >= netManager.m_segments.m_buffer.Length)
            {
                return;
            }

            NetSegment segment = netManager.m_segments.m_buffer[segmentId];
            if ((segment.m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None || segment.Info == null)
            {
                return;
            }

            NetInfo info = segment.Info;

            LaneShiftData.EnsureSegment(
                segmentId,
                segment.m_startNode,
                segment.m_endNode,
                info.name,
                info.m_lanes.Length);

            ClearRows();
            currentSegment = segmentId;

            string name = info.name ?? "Network";
            if (name.Length > 34)
            {
                name = name.Substring(0, 34) + "...";
            }

            titleLabel.text = "Segment " + segmentId + " — " + name;

            float y = HeaderHeight;

            for (int laneIndex = 0; laneIndex < info.m_lanes.Length; laneIndex++)
            {
                NetInfo.Lane lane = info.m_lanes[laneIndex];
                int capturedLaneIndex = laneIndex;

                string caption =
                    "#" + laneIndex + "  " +
                    LaneTypeName(lane) +
                    "  pos " + lane.m_position.ToString("0.0", CultureInfo.InvariantCulture);

                UILabel label = LaneShiftUI.MakeLabel(
                    panel,
                    caption,
                    250f,
                    new Vector3(14f, y + 4f));

                UIButton minus = LaneShiftUI.MakeButton(
                    panel,
                    "−",
                    28f,
                    26f,
                    new Vector3(270f, y));

                UITextField field = LaneShiftUI.MakeField(
                    panel,
                    72f,
                    26f,
                    new Vector3(303f, y));

                UIButton plus = LaneShiftUI.MakeButton(
                    panel,
                    "+",
                    28f,
                    26f,
                    new Vector3(380f, y));

                rows.Add(label);
                rows.Add(minus);
                rows.Add(field);
                rows.Add(plus);

                field.text = Format(LaneShiftData.GetShift(segmentId, capturedLaneIndex));

                minus.eventClick += delegate(UIComponent component, UIMouseEventParameter eventParam)
                {
                    float current = Parse(
                        field.text,
                        LaneShiftData.GetShift(currentSegment, capturedLaneIndex));
                    SetLane(capturedLaneIndex, field, current - Step);
                };

                plus.eventClick += delegate(UIComponent component, UIMouseEventParameter eventParam)
                {
                    float current = Parse(
                        field.text,
                        LaneShiftData.GetShift(currentSegment, capturedLaneIndex));
                    SetLane(capturedLaneIndex, field, current + Step);
                };

                field.eventTextSubmitted += delegate(UIComponent component, string value)
                {
                    SetLane(
                        capturedLaneIndex,
                        field,
                        Parse(value, LaneShiftData.GetShift(currentSegment, capturedLaneIndex)));
                };

                y += RowHeight;
            }

            UIButton reset = LaneShiftUI.MakeButton(
                panel,
                "Reset all lanes",
                145f,
                28f,
                new Vector3(14f, y + 7f));

            reset.eventClick += delegate(UIComponent component, UIMouseEventParameter eventParam)
            {
                LaneShiftData.ResetSegment(currentSegment);
                Show(currentSegment);
            };

            rows.Add(reset);

            panel.height = Mathf.Max(150f, y + FooterHeight);
            panel.isVisible = true;
            panel.BringToFront();
        }
    }

    // =====================================================================
    // On-screen tool button
    // =====================================================================
    public static class LaneShiftButton
    {
        private static UIButton button;

        public static void Create()
        {
            if (button != null)
            {
                return;
            }

            UIView view = UIView.GetAView();
            if (view == null)
            {
                return;
            }

            // IMPORTANT: use AddUIComponent so the button is correctly owned
            // by the CS1 UI hierarchy. Raw GameObject.AddComponent is not the
            // normal way to create ColossalFramework.UI controls.
            button = view.AddUIComponent<UIButton>();
            button.name = "LaneShiftButton";
            button.text = "Lane Shift";
            button.width = 92f;
            button.height = 30f;
            button.relativePosition = new Vector3(10f, 90f);
            button.normalBgSprite = "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.pressedBgSprite = "ButtonMenuPressed";
            button.disabledBgSprite = "ButtonMenuDisabled";
            button.textColor = new Color32(255, 255, 255, 255);
            button.textScale = 0.88f;
            button.textHorizontalAlignment = UIHorizontalAlignment.Center;
            button.textVerticalAlignment = UIVerticalAlignment.Middle;

            button.eventClick += delegate(UIComponent component, UIMouseEventParameter eventParam)
            {
                LaneShiftTool.Toggle();
            };
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

    // =====================================================================
    // Hotkey
    // =====================================================================
    public class LaneShiftKeys : MonoBehaviour
    {
        private void Update()
        {
            if (UIView.HasInputFocus())
            {
                return;
            }

            bool ctrl =
                Input.GetKey(KeyCode.LeftControl) ||
                Input.GetKey(KeyCode.RightControl);

            if (ctrl && Input.GetKeyDown(KeyCode.L))
            {
                LaneShiftTool.Toggle();
            }
        }
    }

    // =====================================================================
    // Tool: click a network segment, then edit its lanes in the panel
    // =====================================================================
    public class LaneShiftTool : ToolBase
    {
        public static LaneShiftTool Instance;

        private ushort hovered;
        private ushort selected;

        public static void Create()
        {
            if (Instance != null)
            {
                return;
            }

            ToolController controller = ToolsModifierControl.toolController;
            if (controller == null)
            {
                return;
            }

            Instance = controller.gameObject.AddComponent<LaneShiftTool>();
        }

        public static void Remove()
        {
            if (Instance == null)
            {
                return;
            }

            Deactivate();
            UnityEngine.Object.Destroy(Instance);
            Instance = null;
        }

        public static void Toggle()
        {
            if (Instance == null)
            {
                return;
            }

            ToolController controller = ToolsModifierControl.toolController;
            if (controller == null)
            {
                return;
            }

            if (controller.CurrentTool == Instance)
            {
                Deactivate();
            }
            else
            {
                controller.CurrentTool = Instance;
            }
        }

        public static void Deactivate()
        {
            if (Instance != null &&
                ToolsModifierControl.toolController != null &&
                ToolsModifierControl.toolController.CurrentTool == Instance)
            {
                ToolsModifierControl.SetTool<DefaultTool>();
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

                if (UIView.IsInsideUI() || !Cursor.visible || Camera.main == null)
                {
                    return;
                }

                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                float length = Camera.main.farClipPlane;

                ToolBase.RaycastInput input = new ToolBase.RaycastInput(ray, length);
                input.m_netService = new RaycastService(
                    ItemClass.Service.None,
                    ItemClass.SubService.None,
                    ItemClass.Layer.Default);
                input.m_ignoreSegmentFlags = NetSegment.Flags.None;
                input.m_ignoreTerrain = true;

                ToolBase.RaycastOutput output;
                if (RayCast(input, out output))
                {
                    hovered = output.m_netSegment;
                }

                if (hovered != 0 && Input.GetMouseButtonDown(0))
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
                {
                    RenderSegment(
                        cameraInfo,
                        hovered,
                        new Color(0.2f, 0.5f, 1f, 0.6f));
                }

                if (selected != 0)
                {
                    RenderSegment(
                        cameraInfo,
                        selected,
                        new Color(0.2f, 1f, 0.4f, 0.35f));
                    RenderLanes(cameraInfo, selected);
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private static void RenderSegment(
            RenderManager.CameraInfo cameraInfo,
            ushort segmentId,
            Color color)
        {
            NetManager netManager = Singleton<NetManager>.instance;
            if (netManager == null || segmentId == 0 || segmentId >= netManager.m_segments.m_buffer.Length)
            {
                return;
            }

            NetSegment segment = netManager.m_segments.m_buffer[segmentId];
            if ((segment.m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None ||
                segment.Info == null)
            {
                return;
            }

            Bezier3 bezier = default(Bezier3);
            bezier.a = netManager.m_nodes.m_buffer[segment.m_startNode].m_position;
            bezier.d = netManager.m_nodes.m_buffer[segment.m_endNode].m_position;

            bool startMiddle =
                (netManager.m_nodes.m_buffer[segment.m_startNode].m_flags & NetNode.Flags.Middle) != NetNode.Flags.None;
            bool endMiddle =
                (netManager.m_nodes.m_buffer[segment.m_endNode].m_flags & NetNode.Flags.Middle) != NetNode.Flags.None;

            NetSegment.CalculateMiddlePoints(
                bezier.a,
                segment.m_startDirection,
                bezier.d,
                segment.m_endDirection,
                startMiddle,
                endMiddle,
                out bezier.b,
                out bezier.c);

            float halfWidth = segment.Info.m_halfWidth;

            Singleton<ToolManager>.instance.m_drawCallData.m_overlayCalls++;
            RenderManager.instance.OverlayEffect.DrawBezier(
                cameraInfo,
                color,
                bezier,
                halfWidth * 2f,
                halfWidth,
                halfWidth,
                -1f,
                1024f,
                false,
                true);
        }

        private static void RenderLanes(RenderManager.CameraInfo cameraInfo, ushort segmentId)
        {
            NetManager netManager = Singleton<NetManager>.instance;
            if (netManager == null || segmentId == 0 || segmentId >= netManager.m_segments.m_buffer.Length)
            {
                return;
            }

            NetInfo info = netManager.m_segments.m_buffer[segmentId].Info;
            if (info == null)
            {
                return;
            }

            uint laneId = netManager.m_segments.m_buffer[segmentId].m_lanes;

            for (int laneIndex = 0;
                 laneIndex < info.m_lanes.Length && laneId != 0;
                 laneIndex++)
            {
                float width = Mathf.Max(0.6f, info.m_lanes[laneIndex].m_width * 0.5f);

                Singleton<ToolManager>.instance.m_drawCallData.m_overlayCalls++;
                RenderManager.instance.OverlayEffect.DrawBezier(
                    cameraInfo,
                    new Color(1f, 0.9f, 0.2f, 0.9f),
                    netManager.m_lanes.m_buffer[laneId].m_bezier,
                    width,
                    0f,
                    0f,
                    -1f,
                    1024f,
                    false,
                    true);

                laneId = netManager.m_lanes.m_buffer[laneId].m_nextLane;
            }
        }
    }

    // =====================================================================
    // Lifecycle / save data
    // =====================================================================
    public class LaneShiftLoading : LoadingExtensionBase
    {
        private static GameObject keysObject;

        public override void OnLevelLoaded(LoadMode mode)
        {
            base.OnLevelLoaded(mode);

            if (mode != LoadMode.NewGame &&
                mode != LoadMode.LoadGame &&
                mode != LoadMode.NewGameFromScenario)
            {
                return;
            }

            try
            {
                LaneShiftTool.Create();
                LaneShiftPanel.Create();
                LaneShiftButton.Create();
                LaneShiftData.RebuildAllSavedSegments();

                if (keysObject == null)
                {
                    keysObject = new GameObject("LaneShiftKeys");
                    keysObject.AddComponent<LaneShiftKeys>();
                }
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

            base.OnLevelUnloading();
        }
    }

    public class LaneShiftSerialization : SerializableDataExtensionBase
    {
        private const string Key = "LaneShift_v1";

        public override void OnLoadData()
        {
            try
            {
                LaneShiftData.Deserialize(serializableDataManager.LoadData(Key));
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public override void OnSaveData()
        {
            try
            {
                LaneShiftData.PruneInvalidRecords();
                byte[] data = LaneShiftData.Serialize();

                if (data != null)
                {
                    serializableDataManager.SaveData(Key, data);
                }
                else
                {
                    serializableDataManager.EraseData(Key);
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}
