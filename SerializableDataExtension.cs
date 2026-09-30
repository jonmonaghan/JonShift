using ICities;
using UnityEngine;

namespace LaneShifter
{
    public class LaneShiftSerializableData : SerializableDataExtensionBase
    {
        private const string DATA_ID = "LaneShifter_v1";

        public override void OnLoadData()
        {
            try
            {
                // OnLoadData fires BEFORE OnLevelLoaded, so Instance doesn't exist yet.
                // Stash the raw bytes; LaneShiftLoading.OnLevelLoaded picks them up.
                LaneShiftManager.PendingLoadData = serializableDataManager.LoadData(DATA_ID);
                Debug.Log($"[LaneShifter] OnLoadData: stashed {LaneShiftManager.PendingLoadData?.Length ?? 0} bytes.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[LaneShifter] OnLoadData error: " + ex);
            }
        }

        public override void OnSaveData()
        {
            try
            {
                byte[] data = LaneShiftManager.Instance?.Serialize();
                if (data != null)
                {
                    serializableDataManager.SaveData(DATA_ID, data);
                    Debug.Log($"[LaneShifter] OnSaveData: saved {data.Length} bytes.");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[LaneShifter] OnSaveData error: " + ex);
            }
        }
    }
}
