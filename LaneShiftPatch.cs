using HarmonyLib;

namespace LaneShifter
{
    [HarmonyPatch(typeof(NetSegment), nameof(NetSegment.UpdateLanes))]
    public static class NetSegmentUpdateLanesPatch
    {
        public static void Postfix(ushort segmentID)
        {
            LaneShiftManager.Instance?.ApplyShifts(segmentID);
        }
    }
}
