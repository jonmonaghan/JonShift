using System;
using System.IO;
using System.Reflection;
using ColossalFramework.UI;
using UnityEngine;

namespace LaneShifter
{
    public class LaneShiftButton : UIButton
    {
        public static LaneShiftButton Instance { get; private set; }
        private bool _toolActive;

        public override void Start()
        {
            base.Start();
            Instance = this;

            name            = "LaneShifterButton";
            width           = 40f;
            height          = 40f;
            tooltip         = "Lane Shifter";
            playAudioEvents = true;

            // Background sprites
            normalBgSprite   = "OptionBase";
            hoveredBgSprite  = "OptionBaseHovered";
            pressedBgSprite  = "OptionBasePressed";
            focusedBgSprite  = "OptionBaseFocused";
            disabledBgSprite = "OptionBaseDisabled";

            // Try to load the icon PNG and apply it as foreground sprite
            Texture2D icon = LoadIcon();
            if (!object.ReferenceEquals(icon, null))
            {
                atlas              = CreateAtlas(icon);
                normalFgSprite     = "icon";
                hoveredFgSprite    = "icon";
                pressedFgSprite    = "icon";
                focusedFgSprite    = "icon";
                text               = "";
            }
            else
            {
                // Fallback: text label
                text       = "LS";
                textScale  = 0.7f;
                textColor  = Color.white;
            }

            Vector2 res = GetUIView().GetScreenResolution();
            absolutePosition = new Vector3(12f, res.y - 120f);

            UIDragHandle drag = AddUIComponent<UIDragHandle>();
            drag.width            = width;
            drag.height           = height;
            drag.relativePosition = Vector3.zero;
            drag.target           = this;

            isVisible = LaneShiftSettings.ShowStandaloneButton.value;

            eventClicked += (c, p) =>
            {
                if (object.ReferenceEquals(LaneShiftTool.Instance, null)) return;
                if (object.ReferenceEquals(ToolsModifierControl.toolController, null)) return;
                if (object.ReferenceEquals(ToolsModifierControl.toolController.CurrentTool, LaneShiftTool.Instance))
                    LaneShiftTool.DisableTool();
                else
                    LaneShiftTool.EnableTool();
            };
        }

        public override void Update()
        {
            base.Update();
            bool active = !object.ReferenceEquals(LaneShiftTool.Instance, null)
                       && !object.ReferenceEquals(ToolsModifierControl.toolController, null)
                       && object.ReferenceEquals(
                              ToolsModifierControl.toolController.CurrentTool,
                              LaneShiftTool.Instance);
            if (active != _toolActive)
            {
                _toolActive     = active;
                normalBgSprite  = active ? "OptionBaseFocused" : "OptionBase";
                focusedBgSprite = active ? "OptionBaseFocused" : "OptionBase";
            }
        }

        public override void OnDestroy()
        {
            if (object.ReferenceEquals(Instance, this)) Instance = null;
            base.OnDestroy();
        }

        // ---- Icon helpers ----

        private static Texture2D LoadIcon()
        {
            try
            {
                string folder   = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string iconPath = Path.Combine(folder, "icon.png");
                if (File.Exists(iconPath))
                {
                    byte[]    buf = File.ReadAllBytes(iconPath);
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                    if (tex.LoadImage(buf)) // LoadImage auto-resizes
                    {
                        tex.Apply();
                        Debug.Log("[LaneShifter] Button icon loaded (" + tex.width + "x" + tex.height + ").");
                        return tex;
                    }
                }
                Debug.LogWarning("[LaneShifter] icon.png not found or failed to load from " + folder);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LaneShifter] Button icon load error: " + ex.Message);
            }
            return null;
        }

        private static UITextureAtlas CreateAtlas(Texture2D icon)
        {
            var atlas = ScriptableObject.CreateInstance<UITextureAtlas>();
            atlas.name = "LaneShifterAtlas";

            var shader = Shader.Find("UI/Default UI Shader");
            if (object.ReferenceEquals(shader, null))
                shader = Shader.Find("Sprites/Default");

            var mat = new Material(shader) { mainTexture = icon };
            atlas.material = mat;

            atlas.AddSprite(new UITextureAtlas.SpriteInfo
            {
                name    = "icon",
                texture = icon,
                region  = new Rect(0f, 0f, 1f, 1f)
            });
            return atlas;
        }
    }
}
