using System;
using System.Collections.Generic;
using Godot;
using TinyWorks.Sim;

namespace TinyWorks.View;

// All art is painted here pixel by pixel into textures, once at startup. Tiles are 16 px.
public sealed class Canvas
{
    public readonly int W, H;
    readonly byte[] _px;
    public Canvas(int w, int h) { W = w; H = h; _px = new byte[w * h * 4]; }

    public void P(int x, int y, Color c)
    {
        if ((uint)x >= W || (uint)y >= H) return;
        int i = (y * W + x) * 4;
        _px[i] = (byte)(c.R * 255); _px[i + 1] = (byte)(c.G * 255); _px[i + 2] = (byte)(c.B * 255); _px[i + 3] = (byte)(c.A * 255);
    }

    public void R(int x, int y, int w, int h, Color c) { for (int j = y; j < y + h; j++) for (int i = x; i < x + w; i++) P(i, j, c); }

    // Filled rect with a 1 px outline and a lighter top edge: the basic "chunky" shape.
    public void Box(int x, int y, int w, int h, Color fill, Color outline)
    {
        R(x, y, w, h, outline);
        R(x + 1, y + 1, w - 2, h - 2, fill);
        R(x + 1, y + 1, w - 2, 1, fill.Lightened(0.25f));
        R(x + 1, y + h - 2, w - 2, 1, fill.Darkened(0.2f));
    }

    public ImageTexture Tex() => ImageTexture.CreateFromImage(Image.CreateFromData(W, H, false, Image.Format.Rgba8, _px));
}

public static class Sprites
{
    public const int T = 16;
    public static readonly Color Ink = new("2a2433"), Metal = new("8a93a6"), MetalDark = new("5d6478"), Accent = new("f2a65a"), Glow = new("ffd36b");
    public static readonly Color[] Ground = { new("71905b"), new("6b8a56"), new("77965f") };

    public static ImageTexture[] Belt;   // 4 animation frames, output to the right
    public static ImageTexture BeltCurve; // input from below, output to the right
    public static ImageTexture Miner, Furnace, InserterBase, Hub, Assembler, Chest, Coin;
    public static readonly Dictionary<string, ImageTexture[]> BeltFrames = new(); // per belt type, 4 animation frames
    public static readonly Dictionary<string, ImageTexture> BeltCurves = new();
    public static readonly Dictionary<string, ImageTexture> Stubs = new(); // half belt joining from a side
    public static ImageTexture BigPole;
    public static ImageTexture Crossing, Pole, Generator, Splitter, Tunnel, PipeTunnel, Pump, Refinery;
    public static readonly Color PipeBody = new("7f8796");
    public static readonly Color Wire = new("3a2a24"), Spark = new("fff3a0");

    // Wooden pole with a crossbar; wires attach at the insulators near the top.
    static Canvas PaintPole()
    {
        var c = new Canvas(T, T);
        var wood = new Color("9a6a42");
        c.R(5, 14, 6, 2, new Color(0, 0, 0, 0.25f)); // shadow
        c.R(6, 2, 4, 14, Ink); c.R(7, 3, 2, 12, wood); c.P(7, 4, wood.Lightened(0.3f));
        c.R(2, 3, 12, 3, Ink); c.R(3, 4, 10, 1, wood.Lightened(0.15f));
        c.R(3, 1, 2, 3, Ink); c.P(3, 2, Glow); c.R(11, 1, 2, 3, Ink); c.P(12, 2, Glow); // insulators
        return c;
    }

    // Steel lattice mast, taller than the wooden pole, with a wide crossbar and glowing insulators.
    static Canvas PaintBigPole(Color beacon, bool super)
    {
        var c = new Canvas(T, T);
        var steel = new Color("8a93a6");
        c.R(4, 14, 8, 2, new Color(0, 0, 0, 0.25f)); // shadow
        c.R(5, 2, 6, 14, Ink); c.R(6, 3, 4, 12, steel.Darkened(0.25f));
        for (int y = 3; y < 15; y += 3) { c.P(6, y, steel); c.P(7, y + 1, steel); c.P(8, y + 1, steel); c.P(9, y + 2, steel); } // bracing
        c.R(0, 1, 16, 3, Ink); c.R(1, 2, 14, 1, steel.Lightened(0.2f));                 // crossbar
        c.R(0, 0, 3, 2, Ink); c.P(1, 0, Glow); c.R(13, 0, 3, 2, Ink); c.P(14, 0, Glow);  // insulators
        c.P(7, 0, Ink); c.P(8, 0, Ink); c.P(7, 1, beacon); c.P(8, 1, beacon);            // beacon: orange, or blue on the super pole
        if (super) { c.R(6, 6, 4, 1, beacon); c.R(6, 11, 4, 1, beacon); }               // glowing bands
        return c;
    }

    // Boiler with a firebox; the flywheel on the right is drawn live so it can spin.
    static Canvas PaintGenerator()
    {
        var c = new Canvas(T, T);
        c.Box(1, 3, 10, 12, new Color("7d8aa0"), Ink);
        c.R(2, 1, 3, 3, Ink); c.R(3, 0, 1, 2, MetalDark);        // stack
        c.Box(3, 10, 6, 4, new Color("2b2224"), Ink);           // firebox, glow drawn live
        c.R(2, 6, 8, 1, MetalDark);
        c.R(11, 5, 4, 8, Ink); c.R(12, 6, 2, 6, MetalDark);       // wheel housing
        return c;
    }
    public static readonly Dictionary<string, ImageTexture> Items = new();
    public static readonly Dictionary<string, ImageTexture> Potions = new();

    // A round flask with a cork, filled with the potion's colour.
    static Canvas PaintPotion(Color col)
    {
        var c = new Canvas(T, T);
        c.R(6, 1, 4, 2, new Color("9a6a42"));                  // cork
        c.R(5, 3, 6, 4, Ink); c.R(6, 3, 4, 4, new Color("dfe8f0")); // neck
        for (int y = 6; y < 16; y++)
            for (int x = 1; x < 15; x++)
            {
                float d = new Vector2(x + 0.5f - 8, y + 0.5f - 10.5f).Length();
                if (d < 5.6f) c.P(x, y, Ink);
                if (d < 4.6f) c.P(x, y, y >= 9 ? col : new Color("dfe8f0"));
            }
        c.P(5, 9, Colors.White); c.P(5, 10, Colors.White);     // shine
        c.P(9, 12, col.Lightened(0.4f)); c.P(7, 13, col.Lightened(0.4f)); // bubbles
        return c;
    }
    public static readonly Dictionary<string, ImageTexture> Icons = new(); // building icons for the UI
    // Chevron colour tells belt tiers apart: yellow, red, blue, violet, green, pink.
    static readonly Dictionary<string, Color> BeltTint = new() { ["belt"] = new("f2c14e"), ["fast_belt"] = new("e8665a"), ["express_belt"] = new("5aa9e8"), ["turbo_belt"] = new("a77fe0"), ["hyper_belt"] = new("5fd08f"), ["ultra_belt"] = new("ff8ad8") };

    public static void Build()
    {
        foreach (var (type, tint) in BeltTint)
        {
            var frames = new ImageTexture[4];
            for (int f = 0; f < 4; f++) frames[f] = PaintBelt(f * 2, tint).Tex();
            BeltFrames[type] = frames;
            BeltCurves[type] = PaintCurve(tint).Tex();
            Stubs[type] = PaintStub(tint).Tex();
            Icons[type] = frames[0];
        }
        Crossing = PaintCrossing().Tex();
        Icons["crossing"] = Crossing;
        Pole = PaintPole().Tex();
        Generator = PaintGenerator().Tex();
        Splitter = PaintSplitter().Tex();
        Tunnel = PaintTunnel().Tex();
        PipeTunnel = PaintPipeTunnel().Tex();
        Pump = PaintPump().Tex();
        Refinery = PaintRefinery().Tex();
        Icons["splitter"] = Splitter; Icons["tunnel"] = Tunnel; Icons["pipe_tunnel"] = PipeTunnel; Icons["pump"] = Pump; Icons["refinery"] = Refinery;
        Icons["pipe"] = PaintPipeIcon().Tex();
        Icons["pole"] = Pole;
        Icons["big_pole"] = BigPole = PaintBigPole(Accent, false).Tex();
        Icons["super_pole"] = PaintBigPole(new Color("5aa9e8"), true).Tex();
        Icons["generator"] = Generator;
        Belt = BeltFrames["belt"];
        BeltCurve = BeltCurves["belt"];
        Miner = PaintMiner().Tex();
        Furnace = PaintFurnace().Tex();
        InserterBase = PaintInserterBase().Tex();
        Hub = PaintHub().Tex();
        Assembler = PaintAssembler().Tex();
        Chest = PaintChest().Tex();
        Coin = PaintCoin().Tex();
        foreach (var id in Defs.Items.Keys) Items[id] = PaintItem(id).Tex();
        foreach (var p in Defs.Potions) Potions[p.Id] = PaintPotion(p.Color).Tex();
        Icons["miner"] = Miner; Icons["furnace"] = Furnace; Icons["hub"] = Hub; Icons["assembler"] = Assembler; Icons["chest"] = Chest;
        Icons["inserter"] = PaintInserterIcon().Tex();
        Icons["source"] = PaintSource().Tex();
        Icons["void"] = PaintVoid().Tex();
        Icons["solar"] = PaintSolar().Tex();
        Icons["lightning_rod"] = PaintLightningRod().Tex();
        // A higher tier without its own art is its base with one pip per tier in the top-left corner.
        foreach (var f in Defs.TierFamilies)
            for (int t = 1; t < f.Length; t++)
                if (!Icons.ContainsKey(f[t])) Icons[f[t]] = WithPips(Icons[f[0]], t);
    }

    static ImageTexture WithPips(ImageTexture tex, int n)
    {
        var img = tex.GetImage();
        for (int k = 0; k < n; k++)
        {
            int x = 1 + k * 3;
            img.FillRect(new Rect2I(x - 1, 0, 4, 4), Ink);
            img.FillRect(new Rect2I(x, 1, 2, 2), Glow);
        }
        return ImageTexture.CreateFromImage(img);
    }

    // Solar array: a tilted blue panel on two short legs.
    static Canvas PaintSolar()
    {
        var c = new Canvas(T, T);
        var blue = new Color("4a6fd0");
        c.R(4, 12, 2, 3, Ink); c.R(10, 12, 2, 3, Ink);
        c.Box(1, 2, 14, 11, blue, Ink);
        for (int x = 5; x < 14; x += 4) c.R(x, 3, 1, 9, blue.Darkened(0.45f));
        c.R(2, 7, 12, 1, blue.Darkened(0.45f));
        c.P(3, 4, Colors.White); c.P(4, 4, Colors.White); c.P(3, 5, Colors.White);
        return c;
    }

    // Lightning rod: a copper mast with a bright tip on a steel foot.
    static Canvas PaintLightningRod()
    {
        var c = new Canvas(T, T);
        var copper = new Color("e0894f");
        c.Box(4, 12, 8, 3, MetalDark, Ink);
        c.R(7, 3, 2, 9, copper); c.R(6, 3, 1, 9, Ink); c.R(9, 3, 1, 9, Ink);
        c.R(7, 1, 2, 2, new Color("e8c15a")); c.P(7, 0, Ink); c.P(8, 0, Ink);
        c.R(5, 6, 6, 1, copper.Darkened(0.3f));
        return c;
    }

    // Sandbox item source: a green crate with a chute on the right, where items come out.
    static Canvas PaintSource()
    {
        var c = new Canvas(T, T);
        var green = new Color("62b86a");
        c.Box(1, 2, 12, 12, green, Ink);
        c.R(4, 5, 6, 6, green.Darkened(0.35f));          // window for the item it makes
        c.Box(12, 5, 4, 6, MetalDark, Ink);              // chute
        c.P(14, 7, Glow); c.P(14, 8, Glow);
        return c;
    }

    // Sandbox item void: a dark swirl that swallows anything.
    static Canvas PaintVoid()
    {
        var c = new Canvas(T, T);
        var purple = new Color("6b4a8f");
        for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                float d = new Vector2(x - 7.5f, y - 7.5f).Length();
                if (d > 7.2f) continue;
                c.P(x, y, d > 6.2f ? Ink : d > 4.5f ? purple : d > 2.5f ? purple.Darkened(0.5f) : new Color("100c16"));
            }
        c.R(3, 6, 2, 1, Glow); c.R(11, 9, 2, 1, Glow); // two specks, so it visibly turns
        return c;
    }

    // A belt running into a riveted box that deals items out front, left and right. Output to the right.
    static Canvas PaintSplitter()
    {
        var c = PaintBelt(0, BeltTint["belt"]);
        c.Box(3, 1, 10, 14, MetalDark, Ink);
        c.R(8, 7, 4, 2, Glow); c.R(10, 6, 1, 4, Glow);   // arrow right
        c.R(7, 3, 2, 3, Accent); c.R(7, 10, 2, 3, Accent); // and to both sides
        c.P(4, 2, Glow); c.P(4, 13, Glow);
        return c;
    }

    // Underground belt entrance: track on the left dives into a hood on the right. The exit is drawn mirrored.
    static Canvas PaintTunnel()
    {
        var c = PaintBelt(0, BeltTint["belt"]);
        c.R(9, 0, 7, T, new Color(0, 0, 0, 0));
        c.Box(7, 1, 9, 14, new Color("c9733e"), Ink);   // hood
        c.R(7, 4, 5, 8, Ink); c.R(8, 5, 3, 6, new Color("15121a")); // dark mouth facing the track
        c.P(13, 3, Glow); c.P(13, 12, Glow);
        return c;
    }

    // Underground pipe: a pipe end going into a ground plate on the right. The exit is drawn mirrored.
    static Canvas PaintPipeTunnel()
    {
        var c = new Canvas(T, T);
        c.R(0, 5, 9, 6, Ink); c.R(0, 6, 9, 4, PipeBody); c.R(0, 6, 9, 1, PipeBody.Lightened(0.3f));
        c.Box(8, 2, 7, 12, MetalDark, Ink);
        c.R(9, 6, 3, 4, Ink);
        c.P(13, 3, Glow); c.P(13, 12, Glow);
        return c;
    }

    // A pump on a little well; it pushes water out of its right side. The plunger is drawn live.
    static Canvas PaintPump()
    {
        var c = new Canvas(T, T);
        var water = Defs.Items["water"].Color;
        c.Box(1, 3, 12, 12, new Color("5d7f9e"), Ink);
        c.Box(3, 9, 8, 4, water.Darkened(0.3f), Ink);   // well
        c.P(5, 10, water.Lightened(0.4f));
        c.R(12, 5, 4, 6, Ink); c.R(12, 6, 4, 4, PipeBody); // outlet (right)
        c.R(6, 0, 2, 4, Ink);                             // plunger guide
        return c;
    }

    // Two round tanks on a frame, with a window for the recipe icon. Fluid in on the left, out on the right.
    static Canvas PaintRefinery()
    {
        var c = new Canvas(T, T);
        var body = new Color("8e7fa8");
        c.Box(1, 3, 14, 12, body, Ink);
        c.Box(2, 0, 5, 5, body.Lightened(0.15f), Ink); c.Box(9, 0, 5, 5, body.Lightened(0.15f), Ink); // tanks
        c.Box(4, 6, 8, 8, new Color("2b3040"), Ink);  // window, recipe drawn live
        c.R(0, 9, 2, 3, PipeBody); c.R(14, 9, 2, 3, PipeBody); // ports
        c.P(3, 1, Glow); c.P(10, 1, Glow);
        return c;
    }

    static Canvas PaintPipeIcon()
    {
        var c = new Canvas(T, T);
        c.R(0, 4, T, 8, Ink); c.R(0, 5, T, 6, PipeBody); c.R(0, 5, T, 1, PipeBody.Lightened(0.3f));
        c.R(6, 3, 4, 10, Ink); c.R(7, 4, 2, 8, PipeBody.Darkened(0.2f)); // flange
        return c;
    }

    static Canvas PaintAssembler()
    {
        var c = new Canvas(T, T);
        c.Box(1, 1, 14, 14, new Color("6f8aa6"), Ink);
        c.R(2, 12, 12, 2, Accent);
        for (int i = 3; i < 14; i += 3) c.R(i, 12, 1, 2, Ink);
        c.Box(4, 3, 8, 8, new Color("2b3040"), Ink); // window: the recipe's item is drawn inside, live
        c.P(2, 2, Glow); c.P(13, 2, Glow);           // rivets
        return c;
    }

    static Canvas PaintChest()
    {
        var c = new Canvas(T, T);
        var wood = new Color("b07a4a");
        c.Box(2, 3, 12, 11, wood, Ink);
        c.R(3, 7, 10, 1, wood.Darkened(0.3f));
        c.R(2, 3, 12, 4, Ink); c.R(3, 4, 10, 2, wood.Lightened(0.15f)); // lid
        c.R(7, 6, 2, 3, MetalDark); c.P(7, 7, Glow);                     // latch
        return c;
    }

    static Canvas PaintCoin()
    {
        var c = new Canvas(8, 8);
        var gold = new Color("f7c948");
        c.R(2, 0, 4, 8, Ink); c.R(0, 2, 8, 4, Ink); c.R(1, 1, 6, 6, Ink);
        c.R(2, 1, 4, 6, gold); c.R(1, 2, 6, 4, gold);
        c.R(3, 2, 2, 4, gold.Darkened(0.2f)); c.P(2, 2, Colors.White);
        return c;
    }

    // Rails with bolts, a dark tread whose slats move with the frame, and bright ">" chevrons in the tier colour.
    static Canvas PaintBelt(int shift, Color chev)
    {
        var c = new Canvas(T, T);
        var rail = new Color("7a8094"); var track = new Color("34333f");
        c.R(0, 2, T, 12, Ink);
        c.R(0, 3, T, 1, rail.Lightened(0.3f)); c.R(0, 4, T, 1, rail);
        c.R(0, 11, T, 1, rail); c.R(0, 12, T, 1, rail.Darkened(0.25f));
        for (int x = 1; x < T; x += 4) { c.P(x, 4, rail.Darkened(0.35f)); c.P(x, 11, rail.Darkened(0.35f)); } // bolts
        c.R(0, 5, T, 6, track);
        for (int x = 0; x < T; x++) if ((x - shift + 64) % 4 == 0) c.R(x, 5, 1, 6, track.Darkened(0.35f)); // tread slats
        c.R(0, 5, T, 1, track.Lightened(0.12f));
        for (int k = -1; k < 3; k++) // ">" chevrons every 8 px, shifted per frame
        {
            int x = k * 8 + shift;
            for (int d = 0; d < 3; d++)
            {
                c.P(x + d - 1, 6 + d, chev.Darkened(0.45f)); c.P(x + d - 1, 9 - d, chev.Darkened(0.45f)); // shadow
                c.P(x + d, 6 + d, chev); c.P(x + d, 9 - d, chev);
            }
            c.P(x + 2, 8, chev.Lightened(0.35f));
        }
        return c;
    }

    // A T-junction piece: the joining belt's last few pixels, drawn over the main belt at the side it joins from.
    // Its track replaces the main belt's rail there, so the side line flows in through an opening.
    static Canvas PaintStub(Color chev)
    {
        var c = new Canvas(T, T);
        var rail = new Color("7a8094"); var track = new Color("34333f");
        c.R(0, 2, 6, 12, Ink);
        c.R(0, 3, 6, 1, rail.Lightened(0.3f)); c.R(0, 4, 6, 1, rail);
        c.R(0, 11, 6, 1, rail); c.R(0, 12, 6, 1, rail.Darkened(0.25f));
        c.R(0, 5, 6, 6, track);
        c.P(5, 4, track); c.P(5, 11, track); // round the corners into the main track
        for (int d = 0; d < 3; d++) { c.P(1 + d, 6 + d, chev); c.P(1 + d, 9 - d, chev); }
        return c;
    }

    // Two tracks crossing under a riveted plate.
    static Canvas PaintCrossing()
    {
        var c = new Canvas(T, T);
        var rail = new Color("6b6f80"); var track = new Color("3b3a46");
        c.R(0, 2, T, 12, Ink); c.R(2, 0, 12, T, Ink);
        c.R(0, 5, T, 6, track); c.R(5, 0, 6, T, track);
        c.R(0, 3, 3, 1, rail); c.R(13, 3, 3, 1, rail); c.R(0, 12, 3, 1, rail); c.R(13, 12, 3, 1, rail);
        c.R(3, 0, 1, 3, rail); c.R(3, 13, 1, 3, rail); c.R(12, 0, 1, 3, rail); c.R(12, 13, 1, 3, rail);
        c.Box(4, 4, 8, 8, MetalDark, Ink);
        c.P(5, 5, Glow); c.P(10, 5, Glow); c.P(5, 10, Glow); c.P(10, 10, Glow);
        c.R(7, 6, 2, 4, Accent); c.R(6, 7, 4, 2, Accent); // a little "+" marking
        return c;
    }

    static Canvas PaintCurve(Color tint)
    {
        var c = new Canvas(T, T);
        var rail = new Color("6b6f80").Lerp(tint, 0.35f); var track = new Color("3b3a46");
        for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                float r = new Vector2(x + 0.5f - T, y + 0.5f - T).Length();
                if (r >= 2f && r < 14f) c.P(x, y, Ink);
                if (r >= 3f && r < 5f || r >= 11f && r < 13f) c.P(x, y, rail);
                else if (r >= 5f && r < 11f) c.P(x, y, track);
            }
        return c;
    }

    static Canvas PaintMiner()
    {
        var c = new Canvas(T, T);
        c.Box(1, 1, 13, 14, Metal, Ink);
        c.R(2, 11, 11, 2, Accent);                  // hazard stripe
        for (int i = 2; i < 13; i += 3) c.R(i, 11, 1, 2, Ink);
        c.R(13, 6, 3, 4, Ink); c.R(13, 7, 2, 2, MetalDark); // output chute (right)
        c.Box(4, 3, 7, 7, MetalDark, Ink);           // drill housing
        return c;
    }

    static Canvas PaintFurnace()
    {
        var c = new Canvas(T, T);
        var brick = new Color("b85c45"); var mortar = new Color("8a3f33");
        c.Box(1, 1, 14, 14, brick, Ink);
        for (int y = 3; y < 14; y += 3) c.R(2, y, 12, 1, mortar);
        for (int y = 2; y < 14; y += 3) for (int x = 3 + (y / 3 % 2) * 2; x < 14; x += 4) c.P(x, y, mortar);
        c.Box(4, 7, 8, 6, new Color("3b2a2a"), Ink); // mouth, glow is drawn live
        c.R(6, 0, 4, 2, Ink);                         // chimney top
        return c;
    }

    static Canvas PaintInserterBase()
    {
        var c = new Canvas(T, T);
        c.Box(4, 4, 8, 8, MetalDark, Ink);
        c.R(6, 6, 4, 4, Accent);
        return c;
    }

    static Canvas PaintInserterIcon()
    {
        var c = PaintInserterBase();
        c.R(7, 1, 2, 7, Ink); c.R(5, 0, 6, 2, Ink); c.R(8, 2, 1, 5, Metal);
        return c;
    }

    // The Hub is the one big, friendly building: a little face that reacts to deliveries.
    static Canvas PaintHub()
    {
        var c = new Canvas(T * 3, T * 3);
        var body = Accent; var roof = new Color("c9733e");
        c.Box(3, 10, 42, 35, body, Ink);
        c.Box(1, 5, 46, 8, roof, Ink);
        c.R(32, 0, 6, 6, Ink); c.R(33, 1, 4, 5, MetalDark);   // chimney
        c.Box(19, 30, 10, 15, new Color("6b4a3a"), Ink);       // door
        c.P(26, 37, Glow);
        for (int s = 0; s < 2; s++)                            // eyes, pupils drawn live
        {
            int x = 11 + s * 20;
            c.Box(x, 17, 7, 8, new Color("fff7e8"), Ink);
        }
        c.R(20, 27, 8, 1, Ink); c.P(19, 26, Ink); c.P(28, 26, Ink); // smile
        return c;
    }

    static Canvas PaintItem(string id)
    {
        var c = new Canvas(8, 8);
        var col = Defs.Items[id].Color;
        if (Defs.Items[id].Fluid) // droplet
        {
            c.R(3, 0, 2, 2, Ink); c.R(2, 2, 4, 1, Ink); c.R(1, 3, 6, 4, Ink); c.R(2, 7, 4, 1, Ink);
            c.R(3, 1, 2, 2, col); c.R(2, 3, 4, 4, col);
            c.P(3, 4, col.Lightened(0.5f));
        }
        else if (id == "plastic") // a smooth bar
        {
            c.Box(0, 2, 8, 4, col, Ink);
            c.R(1, 3, 3, 1, Colors.White);
        }
        else if (id == "lightning_rod") // a copper spike on a base, a spark beside it
        {
            c.R(3, 0, 2, 8, Ink); c.R(3, 1, 1, 6, Defs.Items["copper_rod"].Color); c.R(1, 7, 6, 1, Ink);
            c.P(6, 1, Glow); c.P(5, 2, Glow); c.P(6, 3, Glow);
        }
        else if (id.EndsWith("_screw")) // a head on a threaded shaft
        {
            c.R(1, 0, 6, 2, Ink); c.R(2, 0, 4, 1, col.Lightened(0.3f));
            c.R(3, 2, 2, 6, Ink); for (int y = 2; y < 8; y += 2) { c.P(3, y, col); c.P(4, y + 1, col); }
        }
        else if (id.EndsWith("_nail")) // a flat head, a thin shaft and a point
        {
            c.R(1, 0, 6, 2, Ink); c.R(2, 0, 4, 1, col);
            c.R(3, 2, 2, 5, Ink); c.R(3, 2, 1, 4, col); c.P(3, 7, Ink);
        }
        else if (id.EndsWith("_rod")) // a diagonal bar
        {
            for (int i = 0; i < 7; i++) c.R(i, 6 - i, 2, 2, Ink);
            for (int i = 1; i < 7; i++) c.P(i, 7 - i, col);
            c.P(6, 1, col.Lightened(0.5f));
        }
        else if (id.EndsWith("_plate"))
        {
            c.Box(0, 1, 8, 6, col, Ink);
            c.P(2, 2, col.Lightened(0.5f));
        }
        else if (id == "brick")
        {
            c.Box(0, 2, 8, 5, col, Ink);
            c.R(1, 4, 6, 1, col.Darkened(0.3f));
        }
        else if (id == "gear") // cog: ring with four teeth
        {
            c.R(3, 0, 2, 8, Ink); c.R(0, 3, 8, 2, Ink); c.R(1, 1, 6, 6, Ink);
            c.R(2, 1, 4, 6, col); c.R(1, 2, 6, 4, col); c.R(3, 0, 2, 1, col); c.R(3, 7, 2, 1, col); c.R(0, 3, 1, 2, col); c.R(7, 3, 1, 2, col);
            c.R(3, 3, 2, 2, Ink);
            c.P(2, 2, col.Lightened(0.4f));
        }
        else if (id == "copper_wire") // coil
        {
            c.R(1, 1, 6, 6, Ink);
            for (int y = 2; y < 6; y++) c.R(2, y, 4, 1, y % 2 == 0 ? col : col.Darkened(0.3f));
            c.P(0, 6, col); c.P(7, 1, col);
        }
        else if (id == "circuit") // green board with gold pads
        {
            c.Box(0, 1, 8, 6, col, Ink);
            c.P(2, 3, Glow); c.P(5, 3, Glow); c.P(2, 5, Glow); c.R(3, 4, 2, 1, Ink);
        }
        else if (id == "sand") // a low heap
        {
            c.R(2, 3, 4, 1, Ink); c.R(1, 4, 6, 1, Ink); c.R(0, 5, 8, 2, Ink);
            c.R(3, 4, 2, 1, col); c.R(1, 5, 6, 1, col); c.P(2, 5, col.Lightened(0.4f));
        }
        else if (id == "glass") // a pane with a shine
        {
            c.Box(0, 1, 8, 6, col with { A = 0.8f }, Ink);
            c.P(2, 2, Colors.White); c.P(3, 3, Colors.White); c.P(5, 4, Colors.White);
        }
        else if (id == "lens") // round and clear
        {
            c.R(2, 0, 4, 8, Ink); c.R(0, 2, 8, 4, Ink); c.R(1, 1, 6, 6, Ink);
            c.R(2, 1, 4, 6, col); c.R(1, 2, 6, 4, col); c.P(2, 2, Colors.White); c.P(3, 2, Colors.White);
        }
        else if (id == "battery") // a cell with a cap
        {
            c.R(3, 0, 2, 1, Ink); c.Box(1, 1, 6, 7, col, Ink);
            c.R(2, 3, 4, 1, Glow); c.P(3, 5, Ink); c.P(4, 5, Ink);
        }
        else if (id == "motor") // a box with a shaft sticking out
        {
            c.Box(0, 1, 6, 6, col, Ink);
            c.R(6, 3, 2, 2, Ink); c.P(7, 3, Metal);
            c.R(1, 3, 4, 1, col.Darkened(0.3f)); c.R(1, 5, 4, 1, col.Darkened(0.3f));
        }
        else if (id == "lamp") // a bulb on a screw base
        {
            c.R(2, 0, 4, 1, Ink); c.R(1, 1, 6, 4, Ink); c.R(2, 1, 4, 4, col); c.P(3, 2, Colors.White);
            c.R(2, 5, 4, 3, Ink); c.R(3, 5, 2, 2, Metal);
        }
        else if (id == "smart_chip") // a chip with legs
        {
            c.R(0, 2, 8, 1, Ink); c.R(0, 5, 8, 1, Ink);
            c.Box(1, 1, 6, 6, col, Ink); c.R(3, 3, 2, 2, Glow);
        }
        else if (id == "frame") // a square outline of beams
        {
            c.R(0, 0, 8, 8, Ink); c.R(1, 1, 6, 6, col); c.R(3, 3, 2, 2, Ink); c.R(2, 2, 4, 4, new Color(0, 0, 0, 0));
            c.R(2, 2, 4, 1, Ink); c.R(2, 5, 4, 1, Ink); c.R(2, 2, 1, 4, Ink); c.R(5, 2, 1, 4, Ink);
        }
        else if (id == "toy_robot") // a little head with antenna and eyes
        {
            c.P(4, 0, Glow); c.R(4, 1, 1, 1, Ink);
            c.Box(1, 2, 6, 6, col, Ink); c.P(3, 4, Colors.White); c.P(5, 4, Colors.White); c.R(3, 6, 3, 1, Ink);
        }
        else if (id == "clock") // a round face with hands
        {
            c.R(2, 0, 4, 8, Ink); c.R(0, 2, 8, 4, Ink); c.R(1, 1, 6, 6, Ink);
            c.R(2, 1, 4, 6, new Color("fff7e8")); c.R(1, 2, 6, 4, new Color("fff7e8"));
            c.R(4, 2, 1, 2, Ink); c.R(4, 4, 2, 1, Ink); c.P(0, 3, col); c.P(7, 3, col);
        }
        else if (id == "solar_panel") // blue cells in a grid
        {
            c.Box(0, 1, 8, 6, col, Ink);
            c.R(4, 2, 1, 4, Ink); c.R(1, 4, 6, 1, col.Darkened(0.4f)); c.P(2, 2, Colors.White);
        }
        else if (id == "drone") // body with four rotors
        {
            c.R(0, 0, 3, 1, Ink); c.R(5, 0, 3, 1, Ink); c.R(0, 7, 3, 1, Ink); c.R(5, 7, 3, 1, Ink);
            c.R(1, 1, 1, 6, Ink); c.R(6, 1, 1, 6, Ink);
            c.Box(2, 2, 4, 4, col, Ink); c.P(3, 3, Glow);
        }
        else if (id == "computer") // a monitor on a foot
        {
            c.Box(0, 0, 8, 6, col, Ink); c.R(1, 1, 6, 4, new Color("5aa9e8")); c.P(2, 2, Colors.White);
            c.R(3, 6, 2, 1, Ink); c.R(2, 7, 4, 1, Ink);
        }
        else if (id == "sky_beacon") // a tower with a bright light on top
        {
            c.R(3, 0, 2, 2, Colors.White); c.P(2, 1, col); c.P(5, 1, col);
            c.R(3, 2, 2, 4, Ink); c.R(2, 6, 4, 2, Ink); c.R(3, 6, 2, 1, Metal);
        }
        else if (id == "concrete") // a grey block with grit
        {
            c.Box(0, 1, 8, 6, col, Ink);
            c.P(2, 3, col.Darkened(0.3f)); c.P(5, 2, col.Darkened(0.3f)); c.P(4, 5, col.Darkened(0.3f)); c.R(1, 2, 2, 1, col.Lightened(0.3f));
        }
        else if (id == "fertilizer") // a tied sack
        {
            c.R(3, 0, 2, 2, Ink); c.R(1, 2, 6, 6, Ink);
            c.R(2, 3, 4, 4, col); c.P(3, 1, col.Darkened(0.3f)); c.P(2, 3, col.Lightened(0.4f));
        }
        else if (id == "fuel") // a canister with a spout
        {
            c.R(5, 0, 2, 2, Ink); c.Box(1, 1, 6, 7, col, Ink);
            c.R(2, 2, 2, 1, Ink); c.P(2, 4, col.Lightened(0.4f));
        }
        else if (id == "explosive") // three sticks with a burning fuse
        {
            c.R(1, 3, 6, 5, Ink);
            for (int x = 1; x < 7; x += 2) c.R(x, 4, 1, 3, col);
            c.R(4, 1, 1, 2, Ink); c.P(5, 0, Glow);
        }
        else if (id == "wood") // a log with rings on its end
        {
            c.Box(0, 2, 8, 5, col, Ink);
            c.R(1, 3, 1, 3, col.Lightened(0.35f)); c.P(1, 4, col.Darkened(0.3f));
            c.R(3, 3, 4, 1, col.Darkened(0.25f)); c.R(3, 5, 3, 1, col.Darkened(0.25f));
        }
        else if (id == "quartz" || id == "sparkcrystal") // a pointed crystal, sparkcrystal with a glint
        {
            c.R(3, 0, 2, 1, Ink); c.R(2, 1, 4, 6, Ink); c.R(3, 7, 2, 1, Ink);
            c.R(3, 1, 2, 6, col); c.R(3, 1, 1, 5, col.Lightened(0.4f));
            if (id == "sparkcrystal") { c.P(6, 1, Glow); c.P(7, 0, Glow); c.P(1, 5, Glow); }
        }
        else // ores: a lumpy rock
        {
            c.R(2, 1, 4, 1, Ink); c.R(1, 2, 6, 4, Ink); c.R(2, 6, 4, 1, Ink); c.R(0, 3, 8, 2, Ink);
            c.R(2, 2, 4, 4, col); c.R(1, 3, 6, 2, col);
            c.P(2, 2, col.Lightened(0.45f)); c.P(3, 2, col.Lightened(0.3f));
            c.P(5, 5, col.Darkened(0.3f)); c.P(4, 5, col.Darkened(0.2f));
        }
        return c;
    }

    // One 32x32-tile chunk of ground with ore pebbles, baked into a single texture.
    public const int Chunk = 32;
    public static ImageTexture PaintChunk(World w, Vector2I chunk)
    {
        var c = new Canvas(Chunk * T, Chunk * T);
        for (int ty = 0; ty < Chunk; ty++)
            for (int tx = 0; tx < Chunk; tx++)
            {
                var tile = chunk * Chunk + new Vector2I(tx, ty);
                uint h = Hash(tile.X, tile.Y, w.Seed);
                var g = Ground[h % 3];
                if (((tile.X + tile.Y) & 1) == 0) g = g.Darkened(0.03f); // faint checker helps placing
                c.R(tx * T, ty * T, T, T, g);
                for (int s = 0; s < 3; s++) // grass specks
                {
                    uint hs = Hash(tile.X * 7 + s, tile.Y * 13 - s, w.Seed);
                    c.P(tx * T + (int)(hs % 16), ty * T + (int)(hs / 16 % 16), g.Lightened(0.12f));
                }
                if (w.OreAt(tile) is { } ore) Pebbles(c, tx * T, ty * T, Defs.Items[ore].Color, h);
            }
        return c.Tex();
    }

    static void Pebbles(Canvas c, int ox, int oy, Color col, uint h)
    {
        for (int k = 0; k < 4; k++)
        {
            int x = ox + 1 + (int)((h >> (k * 4)) % 10), y = oy + 1 + (int)((h >> (k * 4 + 2)) % 10);
            c.R(x, y + 1, 5, 3, Ink); c.R(x + 1, y, 3, 5, Ink);
            c.R(x + 1, y + 1, 3, 3, col);
            c.P(x + 1, y + 1, col.Lightened(0.4f));
        }
    }

    public static uint Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2246822519);
            h = (h ^ (h >> 13)) * 1274126177;
            return h ^ (h >> 16);
        }
    }

    // 3x5 pixel digits for floating "+N" numbers in the pixel world.
    static readonly Dictionary<char, string> Glyphs = new()
    {
        ['0'] = "111101101101111", ['1'] = "010110010010111", ['2'] = "111001111100111", ['3'] = "111001111001111",
        ['4'] = "101101111001001", ['5'] = "111100111001111", ['6'] = "111100111101111", ['7'] = "111001010010010",
        ['8'] = "111101111101111", ['9'] = "111101111001111", ['+'] = "000010111010000", ['-'] = "000000111000000",
        ['!'] = "010010010000010", ['?'] = "111001011000010", ['.'] = "000000000000010",
    };

    public static void DrawText(CanvasItem ci, Vector2 pos, string text, Color col)
    {
        for (int n = 0; n < text.Length; n++)
        {
            if (!Glyphs.TryGetValue(text[n], out var g)) continue;
            for (int i = 0; i < 15; i++)
            {
                if (g[i] != '1') continue;
                var p = pos + new Vector2(n * 4 + i % 3, i / 3);
                ci.DrawRect(new Rect2(p + Vector2.One, Vector2.One), Ink);
                ci.DrawRect(new Rect2(p, Vector2.One), col);
            }
        }
    }
}
