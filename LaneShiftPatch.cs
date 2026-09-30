using HarmonyLib;

namespace JonShift
{
    [HarmonyPatch(typeof(NetSegment), nameof(NetSegment.UpdateLanes))]
    public static class NetSegmentUpdateLanesPatch
    {
        // Called by the game after it recalculates all lane beziers for a segment.
        // We intercept here and shift each lane's bezier by the stored offset.
        public static void Postfix(ushort segmentID)
        {
            LaneShiftManager.Instance?.ApplyShifts(segmentID);
        }
    }
}
