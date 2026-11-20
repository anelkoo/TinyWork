using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TinyWorks.Sim;
using TinyWorks.View;

namespace TinyWorks.UI;

// Potions: the Hub's third tab (ready-made and brew-your-own), and the active-potion timers under the coins.
public sealed partial class Hud
{
    static readonly int[] PresetMinutes = { 2, 10 };

    void FillPotions(VBoxContainer body, List<Action> refresh)
    {
        var info = Ui.MakeLabel("", 13, Ui.Muted);
        info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        refresh.Add(() => info.Text = $"Temporary boosts. They only count down while the game runs, and buying more adds time (up to {Defs.PotionMaxMinutes} min). Prices grow with your factory: price level x{W.PriceLevel.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}.");
        body.AddChild(info);

        foreach (var p in Defs.Potions)
        {
            var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            text.AddChild(Ui.MakeLabel(p.Name, 15));
            var desc = Ui.MakeLabel(p.Description, 12, Ui.Muted);
            desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.AddChild(desc);
            var left = Ui.MakeLabel("", 13, p.Color);
            var row = Ui.Row(Ui.Icon(Sprites.Potions[p.Id], 28), text, left);
            foreach (int m in PresetMinutes)
            {
                var buy = Ui.MakeButton("", () => Buy(p, m), Sfx);
                buy.CustomMinimumSize = new Vector2(_mini ? 70 : 104, 36);
                refresh.Add(() =>
                {
                    long cost = W.PotionCost(p.Id, m);
                    buy.Text = $"{m} min · {Hud.Compact(cost)}c";
                    buy.Disabled = W.Coins < cost || W.PotionMinutesLeft(p.Id) + m > Defs.PotionMaxMinutes;
                });
                row.AddChild(buy);
            }
            refresh.Add(() => left.Text = W.Has(p.Id) ? TimeLeft(p.Id) : "");
            body.AddChild(row);
        }

        // Brew your own: any potion, any length.
        body.AddChild(Ui.MakeLabel("Brew your own", 16));
        var kind = new OptionButton { FocusMode = FocusModeEnum.None };
        foreach (var p in Defs.Potions) kind.AddIconItem(Sprites.Potions[p.Id], p.Name);
        var minutes = new HSlider { MinValue = 1, MaxValue = Defs.PotionMaxMinutes, Step = 1, Value = 30, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(120, 0) };
        var minLabel = Ui.MakeLabel("", 14, Ui.Muted);
        minLabel.CustomMinimumSize = new Vector2(64, 0);
        var brew = Ui.MakeButton("", () => Buy(Defs.Potions[kind.Selected], (int)minutes.Value), Sfx);
        brew.CustomMinimumSize = new Vector2(_mini ? 96 : 140, 36);
        refresh.Add(() =>
        {
            var p = Defs.Potions[kind.Selected];
            int m = (int)minutes.Value;
            long cost = W.PotionCost(p.Id, m);
            minLabel.Text = $"{m} min";
            brew.Text = $"Brew · {Hud.Compact(cost)}c";
            brew.Disabled = W.Coins < cost || W.PotionMinutesLeft(p.Id) + m > Defs.PotionMaxMinutes;
        });
        if (_mini)
        {
            body.AddChild(Ui.Row(kind, brew));
            body.AddChild(Ui.Row(minutes, minLabel));
        }
        else body.AddChild(Ui.Row(kind, minutes, minLabel, brew));
    }

    void Buy(Potion p, int minutes)
    {
        if (!W.BuyPotion(p.Id, minutes)) Sfx.Play("error");
    }

    string TimeLeft(string id)
    {
        long s = W.Potions.GetValueOrDefault(id) / Engine.PhysicsTicksPerSecond;
        return $"{s / 60}:{s % 60:00}";
    }

    // Active potions sit under the coin counter with a countdown.
    VBoxContainer _potionRows;
    string _potionSet = "";

    void UpdatePotionTimers()
    {
        var set = string.Join(",", Defs.Potions.Where(p => W.Has(p.Id)).Select(p => p.Id));
        if (set != _potionSet)
        {
            _potionSet = set;
            foreach (var c in _potionRows.GetChildren()) { _potionRows.RemoveChild(c); c.QueueFree(); }
            foreach (var p in Defs.Potions.Where(p => W.Has(p.Id)))
            {
                var row = Ui.Row(Ui.Icon(Sprites.Potions[p.Id], _mini ? 12 : 16), Ui.MakeLabel("", _mini ? 12 : 14, p.Color));
                row.TooltipText = $"{p.Name}: {p.Description}";
                row.SetMeta("id", p.Id);
                _potionRows.AddChild(row);
                Ui.PopIn(row);
            }
        }
        foreach (var c in _potionRows.GetChildren())
            if (c is HBoxContainer row) ((Label)row.GetChild(1)).Text = TimeLeft((string)row.GetMeta("id"));
    }
}
