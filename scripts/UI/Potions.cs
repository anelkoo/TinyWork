using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TinyWorks.Sim;
using TinyWorks.View;

namespace TinyWorks.UI;

// Potions: the Hub's third tab (ready-made and brew-your-own, each with a chosen strength),
// and the active-potion timers under the coins.
public sealed partial class Hud
{
    static readonly int[] PresetMinutes = { 2, 10 };

    // A dropdown of a potion's strengths ("x1.5", "x2", "x3"), starting on its default.
    static OptionButton StrengthPicker(Potion p)
    {
        var pick = new OptionButton { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(92, 36), SizeFlagsVertical = SizeFlags.ShrinkCenter, TooltipText = "Strength. Strengths never stack: the strongest runs first, weaker ones wait." };
        foreach (var s in p.Strengths) pick.AddItem(Defs.X(s));
        pick.Selected = Array.IndexOf(p.Strengths, p.Default);
        return pick;
    }

    void FillPotions(VBoxContainer body, List<Action> refresh)
    {
        var info = Ui.MakeLabel("", 13, Ui.Muted);
        info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        refresh.Add(() => info.Text = $"Temporary boosts that only count down while the game runs. Pick a strength: a stronger potion costs more. Strengths never add up: the strongest one runs first, weaker ones wait and take over after it. Prices grow with your factory: price level {Defs.X(W.PriceLevel).TrimStart('x')}.");
        body.AddChild(info);

        foreach (var p in Defs.Potions)
        {
            var strength = StrengthPicker(p);
            float S() => p.Strengths[strength.Selected];
            var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            text.AddChild(Ui.MakeLabel(p.Name, 15));
            var desc = Ui.MakeLabel("", 12, Ui.Muted);
            desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.AddChild(desc);
            var left = Ui.MakeLabel("", 12, p.Color); // under the description, so every row's buttons line up
            text.AddChild(left);
            refresh.Add(() => { desc.Text = p.Describe(S()); left.Text = ActiveText(p.Id); });
            var buttons = new List<Control> { strength };
            foreach (int m in PresetMinutes)
            {
                var buy = Ui.MakeButton("", () => Buy(p, S(), m), Sfx);
                buy.CustomMinimumSize = new Vector2(_mini ? 96 : 124, 36);
                buy.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                refresh.Add(() =>
                {
                    long cost = W.PotionCost(p.Id, S(), m);
                    buy.Text = $"{m} min · {Compact(cost)}c";
                    buy.Disabled = W.Coins < cost || W.PotionMinutesLeft(p.Id, S()) + m > Defs.PotionMaxMinutes;
                });
                buttons.Add(buy);
            }
            if (_mini)
            {
                body.AddChild(Ui.Row(Ui.Icon(Sprites.Potions[p.Id], 24), text));
                body.AddChild(Ui.Row(buttons.ToArray()));
            }
            else body.AddChild(Ui.Row(new Control[] { Ui.Icon(Sprites.Potions[p.Id], 28), text }.Concat(buttons).ToArray()));
        }

        // Brew your own: any potion, any strength, any length.
        body.AddChild(Ui.MakeLabel("Brew your own", 16));
        var kind = new OptionButton { FocusMode = FocusModeEnum.None };
        foreach (var p in Defs.Potions) kind.AddIconItem(Sprites.Potions[p.Id], p.Name);
        var power = StrengthPicker(Defs.Potions[0]);
        kind.ItemSelected += i => // each potion has its own strengths
        {
            var p = Defs.Potions[i];
            power.Clear();
            foreach (var s in p.Strengths) power.AddItem(Defs.X(s));
            power.Selected = Array.IndexOf(p.Strengths, p.Default);
        };
        var minutes = new HSlider { MinValue = 1, MaxValue = Defs.PotionMaxMinutes, Step = 1, Value = 30, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(100, 0) };
        var minLabel = Ui.MakeLabel("", 14, Ui.Muted);
        minLabel.CustomMinimumSize = new Vector2(60, 0);
        minLabel.HorizontalAlignment = HorizontalAlignment.Right;
        var brew = Ui.MakeButton("", () => Buy(Defs.Potions[kind.Selected], Defs.Potions[kind.Selected].Strengths[power.Selected], (int)minutes.Value), Sfx);
        brew.CustomMinimumSize = new Vector2(_mini ? 96 : 140, 36);
        refresh.Add(() =>
        {
            var p = Defs.Potions[kind.Selected];
            if (power.Selected < 0 || power.Selected >= p.Strengths.Length) return;
            float s = p.Strengths[power.Selected];
            int m = (int)minutes.Value;
            long cost = W.PotionCost(p.Id, s, m);
            minLabel.Text = $"{m} min";
            brew.Text = $"Brew · {Compact(cost)}c";
            brew.Disabled = W.Coins < cost || W.PotionMinutesLeft(p.Id, s) + m > Defs.PotionMaxMinutes;
        });
        if (_mini)
        {
            body.AddChild(Ui.Row(kind, power, brew));
            body.AddChild(Ui.Row(minutes, minLabel));
        }
        else body.AddChild(Ui.Row(kind, power, minutes, minLabel, brew));
    }

    void Buy(Potion p, float strength, int minutes)
    {
        if (!W.BuyPotion(p.Id, strength, minutes)) Sfx.Play("error");
    }

    static string Clock(long ticks)
    {
        long s = ticks / World.TicksPerSecond;
        return $"{s / 60}:{s % 60:00}";
    }

    // "x3 1:59" for the strength that runs now, "+1 waiting" when weaker ones are queued behind it.
    string ActiveText(string id)
    {
        if (!W.Potions.TryGetValue(id, out var list)) return "";
        return $"{Defs.X(list[0].Strength)} {Clock(list[0].Ticks)}" + (list.Count > 1 ? $" +{list.Count - 1} waiting" : "");
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
                row.SetMeta("id", p.Id);
                _potionRows.AddChild(row);
                Ui.PopIn(row);
            }
        }
        foreach (var c in _potionRows.GetChildren())
            if (c is HBoxContainer row)
            {
                var id = (string)row.GetMeta("id");
                var p = Defs.PotionById(id);
                ((Label)row.GetChild(1)).Text = ActiveText(id);
                row.TooltipText = p.Name + ": " + p.Describe(W.Strength(id)) + string.Concat(W.Potions[id].Skip(1).Select(s => $"\nthen {Defs.X(s.Strength)} for {Clock(s.Ticks)}"));
            }
    }
}
