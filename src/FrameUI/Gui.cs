using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace FrameUI
{
    /// <summary>
    /// Drawing helpers shared by all frames.
    /// The game's UnityEngine.IMGUIModule is stripped: GUIStyle only has normal.textColor (no backgrounds,
    /// hover/active states, border, overflow), so nothing here relies on skin textures. Everything is drawn with
    /// GUI.DrawTexture's rounded overload and driven by raw mouse events.
    /// </summary>
    public static class Gui
    {
        /// <summary>
        /// The game uses a linear colour space and IMGUI text comes out darker/more saturated than the hex value.
        /// While enabled, text colours are pre-converted so they match the colour picked in Figma.
        /// </summary>
        public static bool LinearTextFix = true;

        /// <summary>Receives warnings such as missing icons. Defaults to UnityEngine.Debug.LogWarning.</summary>
        public static Action<string> Warn = Debug.LogWarning;

        static readonly Dictionary<(Assembly, string), Texture2D> Icons = new Dictionary<(Assembly, string), Texture2D>();
        static readonly HashSet<(Assembly, string)> FailedIcons = new HashSet<(Assembly, string)>();
        static Texture2D _white;

        static Texture2D White
        {
            get
            {
                if (_white != null) return _white;
                _white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
                return _white;
            }
        }

        public static Color TextColor(Color c)
        {
            if (!LinearTextFix || QualitySettings.activeColorSpace != ColorSpace.Linear) return c;
            var g = c.gamma;
            return new Color(g.r, g.g, g.b, c.a);
        }

        /// <summary>
        /// Loads the PNG embedded in the calling assembly as "Textures.&lt;fileName&gt;" (csproj:
        /// &lt;EmbeddedResource Include="Textures/*.png" LogicalName="Textures.%(Filename)%(Extension)" /&gt;).
        /// Loaded once; returns null (and warns once) if it is missing or invalid.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Texture2D Icon(string fileName) => Icon(Assembly.GetCallingAssembly(), fileName);

        public static Texture2D Icon(Assembly assembly, string fileName)
        {
            var key = (assembly, fileName);
            if (Icons.TryGetValue(key, out var cached) && cached != null) return cached;
            if (FailedIcons.Contains(key)) return null;

            var tex = Load(assembly, fileName);
            if (tex == null)
            {
                FailedIcons.Add(key);
                return null;
            }
            return Icons[key] = tex;
        }

        static Texture2D Load(Assembly assembly, string fileName)
        {
            using (var stream = assembly.GetManifestResourceStream("Textures." + fileName))
            {
                if (stream == null)
                {
                    Warn?.Invoke($"Icon not found: {fileName}");
                    return null;
                }

                var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear
                };
                if (tex.LoadImage(buffer.ToArray())) return tex;

                UnityEngine.Object.Destroy(tex);
                Warn?.Invoke($"Icon is not a valid image: {fileName}");
                return null;
            }
        }

        /// <summary>Wraps text in a colour tag (for FigmaText with RichText = true), with the same linear fix as plain text.</summary>
        public static string Colored(string text, Color c) => $"<color=#{ColorUtility.ToHtmlStringRGB(TextColor(c))}>{text}</color>";

        public static void Round(Rect r, Color c, float radius) => Round(r, c, new Vector4(radius, radius, radius, radius));

        /// <summary>Filled rectangle with per-corner radii (top-left, top-right, bottom-right, bottom-left).</summary>
        public static void Round(Rect r, Color c, Vector4 radii)
        {
            if (r.width <= 0f || r.height <= 0f) return;
            float max = Mathf.Min(r.width, r.height) * 0.5f;
            radii = new Vector4(Mathf.Min(radii.x, max), Mathf.Min(radii.y, max), Mathf.Min(radii.z, max), Mathf.Min(radii.w, max));
            GUI.DrawTexture(r, White, ScaleMode.StretchToFill, true, 0f, c, Vector4.zero, radii);
        }

        public static void Outline(Rect r, Color c, float width = 1f)
        {
            Round(new Rect(r.x, r.y, r.width, width), c, 0f);
            Round(new Rect(r.x, r.yMax - width, r.width, width), c, 0f);
            Round(new Rect(r.x, r.y + width, width, r.height - width * 2f), c, 0f);
            Round(new Rect(r.xMax - width, r.y + width, width, r.height - width * 2f), c, 0f);
        }
    }
}
