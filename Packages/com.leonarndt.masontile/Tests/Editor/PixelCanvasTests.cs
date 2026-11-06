using System;
using MasonTile.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MasonTile.Tests
{
    public class PixelCanvasTests
    {
        static readonly Color32 Red = new(255, 0, 0, 255);

        [Test]
        public void Rect_ClipsToCanvas_AndOriginIsBottomLeft()
        {
            var c = new PixelCanvas(4).Rect(-2, -2, 3, 3, Color.red);
            Assert.That(c.Get(0, 0), Is.EqualTo(Red));
            Assert.That(c.Get(1, 0).a, Is.Zero, "x=1 is outside the clipped 1x1 area");
            Assert.That(c.Get(0, 3).a, Is.Zero);
        }

        [Test]
        public void Border_PaintsEdgesOnly()
        {
            var c = new PixelCanvas(4).Border(Color.red);
            Assert.That(c.Get(0, 2), Is.EqualTo(Red));
            Assert.That(c.Get(3, 3), Is.EqualTo(Red));
            Assert.That(c.Get(1, 1).a, Is.Zero);
        }

        [Test]
        public void Speckle_IsDeterministicPerSeed()
        {
            var a = new PixelCanvas(16).Speckle(Color.red, 0.3f, 7).ToTexture().GetPixels32();
            var b = new PixelCanvas(16).Speckle(Color.red, 0.3f, 7).ToTexture().GetPixels32();
            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void Hex_RejectsTypos()
        {
            Assert.That(PixelCanvas.Hex("#ff0000"), Is.EqualTo(Color.red));
            Assert.Throws<ArgumentException>(() => PixelCanvas.Hex("#ff00zz"));
        }

        [Test]
        public void TileSetBuilder_RejectsReservedAndDuplicateSymbols()
        {
            var builder = new TileSetBuilder("Assets/Unused");
            Assert.Throws<ArgumentException>(() => builder.Add('.', "empty", Tile.ColliderType.None, _ => { }));
            builder.Add('#', "wall", Tile.ColliderType.Grid, _ => { });
            Assert.Throws<ArgumentException>(() => builder.Add('#', "wall2", Tile.ColliderType.Grid, _ => { }));
        }
    }
}
