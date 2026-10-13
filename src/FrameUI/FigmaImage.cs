using UnityEngine;

namespace FrameUI
{
    /// <summary>A frame that draws a texture (icon) or sprite on top of its own fill / border.</summary>
    public sealed class FigmaImage : FigmaFrame
    {
        public Texture2D Texture;
        /// <summary>Drawn instead of Texture when set (cropped out of its atlas).</summary>
        public Sprite Sprite;
        /// <summary>Multiplied with the image's colours.</summary>
        public Color ImageTint = Color.white;
        public ScaleMode Scale = ScaleMode.ScaleToFit;

        public FigmaImage(Rect bounds, Texture2D texture) : base(bounds)
        {
            Texture = texture;
        }

        public FigmaImage(Rect bounds, Sprite sprite) : base(bounds)
        {
            Sprite = sprite;
        }

        protected override void Draw(Rect rect, Color tint)
        {
            base.Draw(rect, tint);
            if (Wireframe) return;

            if (Sprite != null && Sprite.texture != null)
            {
                DrawSprite(rect, ImageTint * tint);
                return;
            }
            if (Texture == null) return;
            GUI.DrawTexture(rect, Texture, Scale, true, 0f, ImageTint * tint, 0f, 0f);
        }

        // GUI.DrawTextureWithTexCoords is missing at runtime and the game's IMGUI only has the 3-argument BeginGroup,
        // so the whole atlas is drawn, offset and clipped by a group (texture y goes up, GUI y goes down).
        void DrawSprite(Rect rect, Color color)
        {
            var tex = Sprite.texture;
            var r = Sprite.textureRect;

            // fit inside rect, keeping the aspect ratio
            float scale = Mathf.Min(rect.width / r.width, rect.height / r.height);
            float w = r.width * scale, h = r.height * scale;
            var dest = new Rect(rect.x + (rect.width - w) * 0.5f, rect.y + (rect.height - h) * 0.5f, w, h);

            GUI.BeginGroup(dest, GUIContent.none, GUIStyle.none);
            var atlas = new Rect(-r.x * scale, -(tex.height - r.yMax) * scale, tex.width * scale, tex.height * scale);
            GUI.DrawTexture(atlas, tex, ScaleMode.StretchToFill, true, 0f, color, 0f, 0f);
            GUI.EndGroup();
        }
    }
}
