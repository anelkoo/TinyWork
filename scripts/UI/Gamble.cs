using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using TinyWorks.Sim;
using TinyWorks.View;

namespace TinyWorks.UI;

// Gambling: the slot machine (the Hub's fourth tab) and the item gamble window.
public sealed partial class Hud
{
    long _slotBet = 10, _stakeN = 1, _targetN = 1; // remembered while the game runs
    string _stake, _target;

    static string Percent(double chance) => (chance * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    // Whole numbers only. Typed text counts once Apply() ran: the buttons take no focus, so they call it first.
    static SpinBox CountBox(long value, double max) => new() { MinValue = 1, MaxValue = max, Step = 1, Value = value, Rounded = true, SelectAllOnFocus = true, CustomMinimumSize = new Vector2(110, 36) };

    static Label Wrapped(string text, int size, Color color)
    {
        var l = Ui.MakeLabel(text, size, color);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }

    void FillSlots(VBoxContainer body, List<Action> refresh)
    {
        body.AddChild(Wrapped("Bet coins and get a multiple of the bet back. Payouts are rounded down. On average 95.2% comes back.", 13, Ui.Muted));
        // The box's limit is fixed: following your coins would reset the text while you type. A bigger bet is cut to what you have.
        var box = CountBox(_slotBet, 1e18);
        box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        box.ValueChanged += v => _slotBet = (long)v;
        var all = Ui.MakeButton("All", () => box.Value = Math.Max(1, W.Coins), Sfx);
        all.TooltipText = "Bet every coin you have";
        var result = Wrapped("", 16, Ui.Muted);
        var spin = Ui.MakeButton("Spin", () =>
        {
            box.Apply();
            long bet = Math.Min((long)box.Value, W.Coins);
            if (W.Spin(bet) is not { } r) { Sfx.Play("error"); return; }
            if (bet < (long)box.Value) box.Value = bet;
            result.Text = $"{Defs.X(r.Tenths / 10f)}: you bet {bet} and got {r.Payout} coins back";
            result.AddThemeColorOverride("font_color", r.Payout > bet ? Ui.Good : r.Payout < bet ? Ui.Bad : Ui.Text);
            Sfx.Play(r.Payout > bet ? "milestone" : "click");
        }, Sfx);
        spin.CustomMinimumSize = new Vector2(_mini ? 80 : 120, 36);
        refresh.Add(() => spin.Disabled = W.Coins < 1);
        body.AddChild(Ui.Row(Ui.Icon(Sprites.Coin, 20), box, all, spin));
        body.AddChild(result);
        body.AddChild(Ui.MakeLabel("Chances", 16));
        var table = new GridContainer { Columns = _mini ? 4 : 6 };
        table.AddThemeConstantOverride("h_separation", 16);
        foreach (var (tenths, percent) in World.SlotTable) table.AddChild(Ui.MakeLabel($"{Defs.X(tenths / 10f)}  {percent}%", 13, Ui.Muted));
        body.AddChild(table);
    }

    OptionButton ItemPick(List<string> ids, ref string chosen)
    {
        var pick = new OptionButton { FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 36), ClipText = true };
        pick.AddThemeConstantOverride("icon_max_width", 20);
        foreach (var id in ids) pick.AddIconItem(Sprites.Items[id], $"{Defs.Items[id].Name}  ({Defs.Items[id].Price}c)");
        var menu = pick.GetPopup();
        for (int i = 0; i < ids.Count; i++) menu.SetItemIconMaxWidth(i, 20);
        menu.MaxSize = new Vector2I(600, (int)Math.Max(120, Size.Y - 40)); // long lists scroll inside the window
        if (!ids.Contains(chosen)) chosen = ids[0];
        pick.Selected = ids.IndexOf(chosen);
        return pick;
    }

    public void OpenGamble()
    {
        Action tick = null;
        var body = OpenPanel("Gamble", 520, () => tick?.Invoke());
        var ordered = Defs.Items.Keys.Where(id => !Defs.IsFluid(id)).OrderBy(Defs.GroupIndex).ToList();
        var have = ordered.Where(id => W.Storage.GetValueOrDefault(id) > 0).ToList();
        if (have.Count == 0) { body.AddChild(Wrapped("Your storage is empty, so there is nothing to bet.", 15, Ui.Text)); return; }
        var targets = ordered.Where(W.CanMake).ToList();

        var stake = ItemPick(have, ref _stake);
        var target = ItemPick(targets, ref _target);
        var stakeN = CountBox(_stakeN, World.MaxGambleCount * 1200.0);
        var targetN = CountBox(_targetN, World.MaxGambleCount);
        var owned = Ui.MakeLabel("", 12, Ui.Muted);
        var chance = Wrapped("", 18, Ui.Accent);
        var result = Wrapped("", 15, Ui.Muted);
        stake.ItemSelected += i => { _stake = have[(int)i]; result.Text = ""; };
        target.ItemSelected += i => { _target = targets[(int)i]; result.Text = ""; };
        stakeN.ValueChanged += v => _stakeN = (long)v;
        targetN.ValueChanged += v => _targetN = (long)v;
        var play = Ui.MakeButton("Gamble", () =>
        {
            stakeN.Apply();
            targetN.Apply();
            long n = (long)stakeN.Value, m = (long)targetN.Value;
            if (W.Gamble(_stake, n, _target, m) is not { } won) { Sfx.Play("error"); return; }
            result.Text = won ? $"You won {m} {Defs.Items[_target].Name}!" : $"Lost. {n} {Defs.Items[_stake].Name} gone.";
            result.AddThemeColorOverride("font_color", won ? Ui.Good : Ui.Bad);
            Sfx.Play(won ? "milestone" : "error");
        }, Sfx);

        body.AddChild(Ui.Row(Ui.MakeLabel("You bet", 15), owned));
        body.AddChild(Ui.Row(stake, stakeN));
        body.AddChild(Ui.MakeLabel("You want", 15));
        body.AddChild(Ui.Row(target, targetN));
        body.AddChild(chance);
        body.AddChild(play);
        body.AddChild(result);
        // Last, so the chance and the button fit the mini window without scrolling.
        body.AddChild(Wrapped("Bet items for other items. The bet is gone either way; if you win, the new items go to storage. Chance = value of your bet / value of what you want.", 13, Ui.Muted));

        tick = () =>
        {
            long n = (long)stakeN.Value, m = (long)targetN.Value, stored = W.Storage.GetValueOrDefault(_stake);
            owned.Text = $"(have {Compact(stored)})";
            bool tooSure = World.GambleTooSure(_stake, n, _target, m);
            chance.Text = tooSure ? $"Over {World.MaxGambleChance}%: bet less."
                : $"Chance: {Percent(World.GambleChance(_stake, n, _target, m))}" + (n > stored ? "  (not enough in storage)" : "");
            play.Disabled = !W.CanGamble(_stake, n, _target, m);
        };
        tick();
    }
}
