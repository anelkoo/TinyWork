using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TinyWorks.Sim;
using TinyWorks.View;

namespace TinyWorks.UI;

// A small card at the top centre (bottom centre in mini mode) describing whatever the mouse is over:
// a building with its contents, progress and power, or an ore patch.
public sealed partial class Hud
{
    PanelContainer _hoverCard;
    TextureRect _hoverIcon;
    Label _hoverTitle, _hoverBody;
    string _hoverShown;

    void BuildHoverCard()
    {
        _hoverCard = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _hoverCard.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel with { A = 0.88f }, Ui.Border, 1, 6, 6));
        var v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", 2);
        _hoverIcon = Ui.Icon(null, 20);
        _hoverTitle = Ui.MakeLabel("", 15, Ui.Accent);
        v.AddChild(Ui.Row(_hoverIcon, _hoverTitle));
        _hoverBody = Ui.MakeLabel("", 13);
        v.AddChild(_hoverBody);
        _hoverCard.AddChild(v);
        AddChild(_hoverCard);
        MoveChild(_hoverCard, 0); // under panels and toasts
    }

    void UpdateHoverCard()
    {
        var tile = View.HoverTile;
        var e = W.At(tile);
        string ore = e == null ? W.OreAt(tile) : null;
        bool show = S.StatOn("tooltips") && !HasPanel && View.Selected == null && (e != null || ore != null)
                    && GetViewport().GuiGetHoveredControl() is null or SubViewportContainer; // not over the HUD (the world itself sits in a container)
        _hoverCard.Visible = show;
        if (!show) { _hoverShown = null; return; }

        var (icon, title, lines) = e != null ? Describe(e) : (Sprites.Items[ore], Defs.Items[ore].Name + " patch",
            new List<string> { "Click to mine it by hand,", "or build a miner on it." });
        var text = string.Join("\n", lines);
        if (_hoverShown == title + text) return;
        bool newTarget = _hoverShown == null || !_hoverShown.StartsWith(title);
        _hoverShown = title + text;
        _hoverIcon.Texture = icon;
        _hoverTitle.Text = title;
        _hoverBody.Text = text;
        _hoverBody.Visible = text.Length > 0;
        _hoverCard.ResetSize();
        _hoverCard.SetAnchorsAndOffsetsPreset(_mini ? LayoutPreset.CenterBottom : LayoutPreset.CenterTop);
        _hoverCard.GrowHorizontal = GrowDirection.Both;
        _hoverCard.GrowVertical = _mini ? GrowDirection.Begin : GrowDirection.End;
        _hoverCard.Position += _mini ? new Vector2(0, -40) : new Vector2(0, 58); // under the top button row
        if (newTarget && Ui.Enabled) Ui.PopIn(_hoverCard);
    }

    (Texture2D Icon, string Title, List<string> Lines) Describe(Entity e)
    {
        var def = Defs.Buildings[e.Type];
        var lines = new List<string>();
        static string N(string id) => Defs.Items[id].Name;
        string Pct(float progress, float total) => $"{Mathf.Clamp(progress / total, 0, 1):P0}";

        switch (e)
        {
            case Belt b:
                lines.Add($"Moves up to {b.Speed * World.TicksPerSecond / Belt.Spacing:0} items/s");
                lines.Add(b.Items.Count == 0 ? "Empty" : $"Carrying {b.Items.Count} item{(b.Items.Count == 1 ? "" : "s")}");
                break;
            case Splitter sp:
                lines.Add("Hands items out front, right and left in turn");
                if (sp.Lane.Count > 0) lines.Add($"Holding {sp.Lane.Count}");
                break;
            case Tunnel tu:
                var partner = W.Partner(tu);
                lines.Add(tu.Exit ? "Exit of an underground belt" : "Entrance of an underground belt");
                lines.Add(partner == null ? $"No partner within {Defs.TunnelRange} tiles" : $"Runs {tu.Length(partner)} tiles under the ground");
                if (!tu.Exit && tu.Lane.Count > 0) lines.Add($"{tu.Lane.Count} items underway");
                break;
            case Crossing x:
                lines.Add("Two lines pass through each other");
                break;
            case Inserter ins:
                lines.Add(ins.Filter == null ? "Takes anything the target accepts" : $"Only takes {N(ins.Filter)}");
                if (ins.Held != null) lines.Add($"Holding {N(ins.Held)}");
                break;
            case Miner m:
                var under = W.OreAt(m.Pos);
                lines.Add(under == null ? "No ore here" : $"Mines {N(under)}: one every {Miner.Interval / W.SpeedOf(m) / World.TicksPerSecond:0.0}s");
                if (m.Buffer != null) lines.Add($"Waiting to drop {N(m.Buffer)}");
                break;
            case Furnace f:
                lines.Add(f.In == null ? "Input: empty" : $"Input: {f.InN} {N(f.In)}");
                lines.Add(f.Out == null ? "Output: empty" : $"Output: {f.OutN} {N(f.Out)}");
                if (f.In != null && Defs.Recipes.FirstOrDefault(r => r.Machine == "furnace" && r.In[0].Item == f.In && W.IsUnlocked(r.Unlock)) is { } fr)
                    lines.Add($"Smelting {N(fr.Out)}: {Pct(f.Progress, fr.Ticks)}");
                break;
            case Assembler a: // also refineries
                if (a.Recipe is not { } r) { lines.Add("No recipe yet: click to pick one"); break; }
                lines.Add($"Makes {r.OutCount} {N(r.Out)}: {Pct(a.Progress, r.Ticks)}");
                lines.Add("Has " + string.Join(", ", r.In.Select(x => $"{a.Inv.GetValueOrDefault(x.Item)}/{x.Count} {N(x.Item)}")));
                lines.Add($"Output: {a.OutN} {N(r.Out)}");
                break;
            case Chest c:
                lines.Add($"{c.Count}/{Chest.Cap} items");
                foreach (var (id, n) in c.Inv.OrderByDescending(x => x.Value).Take(3)) lines.Add($"{n} {N(id)}");
                break;
            case Pole p:
                lines.Add($"Wires up to {W.WireRange(p.Type)} tiles, powers {Defs.PoleReachOf(p.Type) * 2 + 1}x{Defs.PoleReachOf(p.Type) * 2 + 1}");
                if (W.NetOf(p) is { } net)
                    lines.Add($"Network: {net.Gens.Count} generators, {net.Users.Count} machines, {net.Demand:0}/{net.Capacity:0} kW");
                break;
            case Generator g:
                lines.Add($"{g.Fuel} coal waiting" + (g.Gas != null ? ", sparkgas pipe connected" : ""));
                if (W.NetOf(g) is { } gn) lines.Add($"Network uses {gn.Demand:0} of {gn.Capacity:0} kW");
                else lines.Add("No pole nearby");
                break;
            case Solar so:
                lines.Add($"Gives {Defs.SolarKW:0} kW, day and night, no fuel");
                if (W.NetOf(so) is { } sn) lines.Add($"Network uses {sn.Demand:0} of {sn.Capacity:0} kW");
                else lines.Add("No pole nearby");
                break;
            case Pipe pi:
                var fnet = W.FluidOf(pi);
                lines.Add(fnet?.Fluid is { } fl ? $"{N(fl)}: {fnet.Amount:0}/{fnet.Cap:0}" : $"Empty (holds {fnet?.Cap ?? 0:0})");
                if (pi.Type == "pipe_tunnel") lines.Add(W.Partner(pi) == null ? $"No partner within {Defs.PipeTunnelRange} tiles" : "Connected underground");
                break;
            case Pump pu:
                lines.Add($"Pumps {W.PumpRate * W.SpeedOf(pu) * World.TicksPerSecond:0} water/s into the pipe in front");
                break;
            case Hub:
                lines.Add("Central storage: everything delivered here");
                lines.Add("Click to sell, shop and brew potions");
                break;
        }

        if (Defs.UsesPower(e.Type))
            lines.Add(W.PowerBoost(e) > 1.01f ? $"Powered: {Defs.X(Mathf.Snapped(W.PowerBoost(e), 0.1f))} speed"
                : W.NetOf(e) != null ? "On a pole, but no power yet: normal speed" : "No power: works at normal speed");
        if (e.Status is { } st)
            lines.Add(st switch { "blocked" => "Output is full", "norecipe" => "Pick a recipe", _ => "Waiting for input" });
        return (Sprites.Icons.GetValueOrDefault(e.Type), def.Name, lines);
    }
}
