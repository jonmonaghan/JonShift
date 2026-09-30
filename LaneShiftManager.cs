using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using ColossalFramework;
using ColossalFramework.Math;
using UnityEngine;

namespace JonShift
{
    /// <summary>
    /// Stores per-lane lateral shift offsets and applies them after UpdateLanes.
    /// </summary>
    public class LaneShiftManager
    {
        // ---- Singleton ----
        public static LaneShiftManager Instance { get; private set; }
        public static void Create()  { Instance = new LaneShiftManager(); }
        public static void Release() { Instance = null; }

        // laneId -> lateral shift (metres, positive = right relative to road direction)
        private readonly Dictionary<uint, float> _shifts = new Dictionary<uint, float>();

        // ---- Public API ----
        public float GetShift(uint laneId)
        {
            _shifts.TryGetValue(laneId, out float v);
            return v;
        }

        public void SetShift(uint laneId, float shift)
        {
            if (Mathf.Approximately(shift, 0f))
                _shifts.Remove(laneId);
            else
                _shifts[laneId] = shift;
        }

        public bool HasAnyShift(ushort segmentId)
        {
            foreach (uint laneId in GetLaneIds(segmentId))
                if (_shifts.ContainsKey(laneId)) return true;
            return false;
        }

        // Called from Harmony postfix — moves each lane bezier laterally.
        public void ApplyShifts(ushort segmentId)
        {
            if (segmentId == 0) return;

            NetManager nm = Singleton<NetManager>.instance;
            ref NetSegment seg = ref nm.m_segments.m_buffer[segmentId];
            if (seg.Info == null) return;

            uint laneId = seg.m_lanes;
            int laneCount = seg.Info.m_lanes.Length;
            for (int i = 0; i < laneCount && laneId != 0; i++)
            {
                if (_shifts.TryGetValue(laneId, out float shift) && !Mathf.Approximately(shift, 0f))
                {
                    ref NetLane lane = ref nm.m_lanes.m_buffer[laneId];
                    lane.m_bezier = ShiftBezier(lane.m_bezier, shift);
                    lane.UpdateLength();
                }
                laneId = nm.m_lanes.m_buffer[laneId].m_nextLane;
            }
        }

        // Shift a Bezier3 laterally (perpendicular to road, XZ plane only).
        private static Bezier3 ShiftBezier(Bezier3 b, float shift)
        {
            return new Bezier3(
                b.a + GetNormal(b.b - b.a) * shift,
                b.b + GetNormal(b.c - b.a) * shift,
                b.c + GetNormal(b.d - b.b) * shift,
                b.d + GetNormal(b.d - b.c) * shift
            );
        }

        // Perpendicular to direction in XZ plane (points right of direction).
        private static Vector3 GetNormal(Vector3 dir)
        {
            if (dir == Vector3.zero) return Vector3.right;
            dir.y = 0f;
            dir.Normalize();
            return new Vector3(dir.z, 0f, -dir.x);
        }

        // ---- Helpers ----
        public static List<uint> GetLaneIds(ushort segmentId)
        {
            var list = new List<uint>();
            if (segmentId == 0) return list;
            NetManager nm = Singleton<NetManager>.instance;
            ref NetSegment seg = ref nm.m_segments.m_buffer[segmentId];
            if (seg.Info == null) return list;
            uint laneId = seg.m_lanes;
            int count = seg.Info.m_lanes.Length;
            for (int i = 0; i < count && laneId != 0; i++)
            {
                list.Add(laneId);
                laneId = nm.m_lanes.m_buffer[laneId].m_nextLane;
            }
            return list;
        }

        // Trigger game to recalculate lanes for a segment.
        public static void UpdateSegment(ushort segmentId)
        {
            NetManager nm = Singleton<NetManager>.instance;
            nm.UpdateSegment(segmentId);
        }

        // ---- Serialisation (XML) ----
        [XmlRoot("JonShiftData")]
        public class SaveData
        {
            [XmlArray("Lanes"), XmlArrayItem("Lane")]
            public List<LaneEntry> Lanes { get; set; } = new List<LaneEntry>();
        }

        public class LaneEntry
        {
            [XmlAttribute] public uint LaneId;
            [XmlAttribute] public float Shift;
        }

        public byte[] Serialize()
        {
            var data = new SaveData();
            foreach (var kv in _shifts)
                data.Lanes.Add(new LaneEntry { LaneId = kv.Key, Shift = kv.Value });
            if (data.Lanes.Count == 0) return null;

            var xs = new XmlSerializer(typeof(SaveData));
            using var ms = new MemoryStream();
            xs.Serialize(ms, data);
            return ms.ToArray();
        }

        public void Deserialize(byte[] bytes)
        {
            _shifts.Clear();
            if (bytes == null || bytes.Length == 0) return;
            try
            {
                var xs = new XmlSerializer(typeof(SaveData));
                using var ms = new MemoryStream(bytes);
                var data = (SaveData)xs.Deserialize(ms);
                foreach (var e in data.Lanes)
                    _shifts[e.LaneId] = e.Shift;

                // Re-apply all shifts after load.
                var seenSegments = new HashSet<ushort>();
                NetManager nm = Singleton<NetManager>.instance;
                foreach (uint laneId in _shifts.Keys)
                {
                    ushort segId = nm.m_lanes.m_buffer[laneId].m_segment;
                    if (seenSegments.Add(segId))
                        nm.m_segments.m_buffer[segId].UpdateLanes(segId, true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[JonShift] Deserialize error: " + ex);
            }
        }
    }
}
