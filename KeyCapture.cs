using System;
using UnityEngine;

namespace LaneShifter
{
    // Attach to a temporary GameObject; self-destructs after one keypress.
    // Ignores pure modifier keys so the user can hold Shift before pressing the key.
    public class KeyCapture : MonoBehaviour
    {
        private Action<KeyCode, bool, bool, bool> _callback;
        private bool _armed; // wait one frame so the click that started capture doesn't register

        public static void Start(Action<KeyCode, bool, bool, bool> callback)
        {
            var go = new GameObject("LaneShifterKeyCapture");
            var kc = go.AddComponent<KeyCapture>();
            kc._callback = callback;
            kc._armed    = false;
            DontDestroyOnLoad(go);
        }

        void Update()
        {
            if (!_armed) { _armed = true; return; } // skip first frame

            // Escape = cancel
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _callback(KeyCode.None, false, false, false);
                Destroy(gameObject);
                return;
            }

            if (!Input.anyKeyDown) return;

            // Find the non-modifier key that was pressed
            foreach (KeyCode kc in System.Enum.GetValues(typeof(KeyCode)))
            {
                // Skip modifiers, mouse, joystick
                if (kc == KeyCode.LeftShift   || kc == KeyCode.RightShift  ||
                    kc == KeyCode.LeftControl  || kc == KeyCode.RightControl||
                    kc == KeyCode.LeftAlt      || kc == KeyCode.RightAlt    ||
                    kc == KeyCode.LeftCommand  || kc == KeyCode.RightCommand||
                    kc == KeyCode.LeftWindows  || kc == KeyCode.RightWindows||
                    kc == KeyCode.None)
                    continue;

                // Skip mouse buttons
                int kci = (int)kc;
                if (kci >= 323 && kci <= 329) continue; // Mouse0-Mouse6

                if (Input.GetKeyDown(kc))
                {
                    bool shift = Input.GetKey(KeyCode.LeftShift)  || Input.GetKey(KeyCode.RightShift);
                    bool ctrl  = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                    bool alt   = Input.GetKey(KeyCode.LeftAlt)    || Input.GetKey(KeyCode.RightAlt);
                    _callback(kc, shift, ctrl, alt);
                    Destroy(gameObject);
                    return;
                }
            }
        }
    }
}
