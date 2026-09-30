using ICities;
using UnityEngine;

namespace JonShift
{
    public class LaneShiftSerializableData : SerializableDataExtensionBase
    {
        private const string DATA_ID = "JonShift_v1";

        public override void OnLoadData()
        {
            try
            {
                byte[] data = serializableDataManager.LoadData(DATA_ID);
                LaneShiftManager.Instance?.Deserialize(data);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[JonShift] OnLoadData error: " + ex);
            }
        }

        public override void OnSaveData()
        {
            try
            {
                byte[] data = LaneShiftManager.Instance?.Serialize();
                if (data != null)
                    serializableDataManager.SaveData(DATA_ID, data);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[JonShift] OnSaveData error: " + ex);
            }
        }
    }
}
