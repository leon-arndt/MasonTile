using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MasonTile.Editor
{
    /// <summary>
    /// Tiny procedural pixel-art canvas for placeholder sprites. (0, 0) is the bottom-left pixel,
    /// matching Unity texture coordinates. Methods chain, and drawing outside the canvas is clipped.
    /// </summary>
    public sealed class PixelCanvas
    {
        readonly Color32[] m_Pixels;

        public int Size { get; }

        public PixelCanvas(int size = 16)
        {
            if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
            Size = size;
            m_Pixels = new Color32[size * size];
        }

        /// <summary>Parses "#rrggbb" or "#rrggbbaa". Throws on invalid input so typos fail loudly.</summary>
        public static Color Hex(string html)
            => ColorUtility.TryParseHtmlString(html, out var c) ? c : throw new ArgumentException($"Invalid colour '{html}'.");

        public Color32 Get(int x, int y) => m_Pixels[x + y * Size];

        public PixelCanvas Set(int x, int y, Color c)
        {
            if (x >= 0 && y >= 0 && x < Size && y < Size) m_Pixels[x + y * Size] = c;
            return this;
        }

        public PixelCanvas Fill(Color c) => Rect(0, 0, Size, Size, c);

        public PixelCanvas Rect(int x, int y, int width, int height, Color c)
        {
            for (var py = y; py < y + height; py++)
            for (var px = x; px < x + width; px++)
                Set(px, py, c);
            return this;
        }

        public PixelCanvas Border(Color c, int thickness = 1)
        {
            Rect(0, 0, Size, thickness, c);
            Rect(0, Size - thickness, Size, thickness, c);
            Rect(0, 0, thickness, Size, c);
            return Rect(Size - thickness, 0, thickness, Size, c);
        }

        public PixelCanvas Circle(float cx, float cy, float radius, Color c)
        {
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var dx = x + 0.5f - cx;
                var dy = y + 0.5f - cy;
                if (dx * dx + dy * dy <= radius * radius) Set(x, y, c);
            }
            return this;
        }

        /// <summary>Filled triangle (pixel centres inside the triangle are painted).</summary>
        public PixelCanvas Triangle(Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
                var hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
                var hasPos = d1 > 0 || d2 > 0 || d3 > 0;
                if (!(hasNeg && hasPos)) Set(x, y, color);
            }
            return this;

            static float Cross(Vector2 p, Vector2 v1, Vector2 v2) => (p.x - v2.x) * (v1.y - v2.y) - (v1.x - v2.x) * (p.y - v2.y);
        }

        /// <summary>Sprinkles single pixels. Deterministic per seed, so regenerated art is identical.</summary>
        public PixelCanvas Speckle(Color c, float density, int seed, int x = 0, int y = 0, int width = -1, int height = -1)
        {
            if (width < 0) width = Size;
            if (height < 0) height = Size;
            var rng = new System.Random(seed);
            for (var py = y; py < y + height; py++)
            for (var px = x; px < x + width; px++)
                if (rng.NextDouble() < density) Set(px, py, c);
            return this;
        }

        public Texture2D ToTexture()
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels32(m_Pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Writes a PNG at <paramref name="assetPath"/> and imports it as a point-filtered, uncompressed
        /// sprite with one pixel-per-unit per canvas pixel (so one sprite covers one grid cell).
        /// </summary>
        public Sprite SaveSprite(string assetPath, Vector2? pivot = null)
        {
            var dir = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var tex = ToTexture();
            File.WriteAllBytes(assetPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = Size;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = pivot.HasValue ? (int)SpriteAlignment.Custom : (int)SpriteAlignment.Center;
            if (pivot.HasValue) settings.spritePivot = pivot.Value;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }
    }
}
