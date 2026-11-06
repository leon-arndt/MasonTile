using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MasonTile.Editor
{
    /// <summary>
    /// Defines a tileset in code: each entry is a legend symbol, a name, a collider type and a paint
    /// function. <see cref="Build"/> writes the sprites, Tile assets and a <see cref="TileLegend"/>.
    /// Re-running it updates the same assets in place, so GUIDs and scene references are kept.
    /// </summary>
    public sealed class TileSetBuilder
    {
        readonly struct Entry
        {
            public readonly char Symbol;
            public readonly string Name;
            public readonly Tile.ColliderType Collider;
            public readonly Action<PixelCanvas> Paint;

            public Entry(char symbol, string name, Tile.ColliderType collider, Action<PixelCanvas> paint)
            {
                Symbol = symbol;
                Name = name;
                Collider = collider;
                Paint = paint;
            }
        }

        readonly string m_Folder;
        readonly string m_SpriteFolder;
        readonly int m_PixelSize;
        readonly List<Entry> m_Entries = new();

        /// <param name="folder">Project folder for the Tile assets, e.g. "Assets/MyGame/Tiles".</param>
        /// <param name="pixelSize">Canvas size in pixels; one canvas covers one grid cell.</param>
        /// <param name="spriteSubfolder">Subfolder of <paramref name="folder"/> for the PNGs; empty puts them next to the tiles.</param>
        public TileSetBuilder(string folder, int pixelSize = 16, string spriteSubfolder = "Art")
        {
            m_Folder = folder.TrimEnd('/');
            m_SpriteFolder = string.IsNullOrEmpty(spriteSubfolder) ? m_Folder : $"{m_Folder}/{spriteSubfolder.Trim('/')}";
            m_PixelSize = pixelSize;
        }

        /// <summary>Adds a tile. <paramref name="collider"/> None = walk-through, Grid = solid full cell.</summary>
        public TileSetBuilder Add(char symbol, string name, Tile.ColliderType collider, Action<PixelCanvas> paint)
        {
            if (TileLegend.IsReserved(symbol)) throw new ArgumentException($"'{symbol}' is reserved.", nameof(symbol));
            if (m_Entries.Exists(e => e.Symbol == symbol)) throw new ArgumentException($"Symbol '{symbol}' is used twice.", nameof(symbol));
            m_Entries.Add(new Entry(symbol, name, collider, paint));
            return this;
        }

        /// <summary>Creates or updates all sprites and tiles, and the legend at <paramref name="legendPath"/>.</summary>
        public TileLegend Build(string legendPath)
        {
            Directory.CreateDirectory(m_Folder);
            var legend = AssetDatabase.LoadAssetAtPath<TileLegend>(legendPath);
            if (legend == null)
            {
                legend = ScriptableObject.CreateInstance<TileLegend>();
                AssetDatabase.CreateAsset(legend, legendPath);
            }

            foreach (var e in m_Entries)
            {
                var canvas = new PixelCanvas(m_PixelSize);
                e.Paint(canvas);
                var sprite = canvas.SaveSprite($"{m_SpriteFolder}/{e.Name}.png");
                legend.Set(e.Symbol, e.Name, CreateOrUpdateTile($"{m_Folder}/{e.Name}.asset", sprite, e.Collider));
            }

            EditorUtility.SetDirty(legend);
            AssetDatabase.SaveAssets();
            return legend;
        }

        public static Tile CreateOrUpdateTile(string assetPath, Sprite sprite, Tile.ColliderType collider)
        {
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(assetPath);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, assetPath);
            }
            tile.sprite = sprite;
            tile.colliderType = collider;
            EditorUtility.SetDirty(tile);
            return tile;
        }
    }
}
