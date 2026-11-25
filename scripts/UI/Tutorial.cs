using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TinyWorks.Sim;

namespace TinyWorks.UI;

// First-start tutorial: a checklist card at the top right that ticks itself off as you play.
// Progress is saved per world (World.Tutorial). Saves already past milestone 1 skip it.
public sealed partial class Hud
{
    sealed record Step(string Title, string Hint, Func<Hud, bool> Done);

    static readonly Step[] Steps =
    {
        new("Mine by hand", "Click the coloured pebbles on the ground to dig up ore. Mouse wheel zooms, WASD or the middle mouse button moves around.", h => h._tutSeen.Contains("mined")),
        new("Build a miner", "Press 3 and click on ore. Ore comes out on every side, and miners that touch share their outputs. Put a belt next to one. Miners dig forever.", h => h.W.Entities.Any(e => e is Miner)),
        new("Belt it to the Hub", "Press 1 and drag a line of belts from the miner's chute to the big orange Hub. Everything the Hub gets goes into your storage (top left).", h => h._tutSeen.Contains("ore")),
        new("Smelt some plates", "Press 4 and put a furnace at the end of a belt that carries ore. The belt feeds it by itself.", h => h._tutSeen.Contains("smelted")),
        new("Plates to the Hub", "Furnaces need an inserter to take plates out. Press 2, place it next to the furnace facing away (R turns it), and belt the plates to the Hub.", h => h._tutSeen.Contains("plate")),
        new("Earn coins", "Click the Hub and switch on Auto-sell for an item. It always keeps a reserve for building. Coins buy upgrades in the Shop tab.", h => h.W.AutoSell.Count > 0),
        new("Reach a milestone", "The bar at the top shows what the next milestone needs. Deliver it to the Hub to unlock new buildings. Q shows them all.", h => h.W.MilestoneIndex >= 1),
    };

    readonly HashSet<string> _tutSeen = new();
    PanelContainer _tutCard;
    VBoxContainer _tutBody;
    int _tutShown = -2;
    double _tutDoneAt = -1;

    void BuildTutorial()
    {
        if (W.Tutorial == 0 && W.MilestoneIndex > 0) W.Tutorial = -1; // an old factory doesn't need it
        View.OnEvent += e =>
        {
            switch (e.Kind)
            {
                case "mined": _tutSeen.Add("mined"); break;
                case "delivered" when Array.IndexOf(Defs.Ores, e.Id) >= 0: _tutSeen.Add("ore"); break;
                case "delivered" when e.Id.EndsWith("_plate"): _tutSeen.Add("plate"); break;
                case "crafted" when W.At(e.Tile) is Furnace: _tutSeen.Add("smelted"); break;
            }
        };
        _tutCard = new PanelContainer { CustomMinimumSize = new Vector2(300, 0) };
        _tutCard.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        _tutCard.GrowHorizontal = GrowDirection.Begin;
        _tutCard.Position += new Vector2(-10, 58);
        _tutBody = new VBoxContainer();
        _tutBody.AddThemeConstantOverride("separation", 4);
        _tutCard.AddChild(_tutBody);
        AddChild(_tutCard);
        MoveChild(_tutCard, 0); // under the panels and toasts
        ShowTutorial();
    }

    bool TutorialActive => W.Tutorial >= 0 && (W.Tutorial < Steps.Length || _tutDoneAt >= 0);

    void ShowTutorial()
    {
        if (_tutCard != null) _tutCard.Visible = TutorialActive && !_mini && !W.Free && _welcome == null; // same corner as the welcome card
    }

    void UpdateTutorial()
    {
        if (!TutorialActive) return;
        int before = W.Tutorial;
        while (W.Tutorial < Steps.Length && Steps[W.Tutorial].Done(this)) W.Tutorial++;
        if (W.Tutorial != before)
        {
            View.Cheer();
            Sfx.Play("coin");
            if (W.Tutorial == Steps.Length) _tutDoneAt = Time.GetTicksMsec() / 1000.0;
        }
        if (_tutDoneAt >= 0 && Time.GetTicksMsec() / 1000.0 - _tutDoneAt > 5) { _tutDoneAt = -1; ShowTutorial(); return; }
        if (_tutShown == W.Tutorial) return;
        _tutShown = W.Tutorial;
        RebuildTutorial(W.Tutorial != before);
    }

    void RebuildTutorial(bool advanced)
    {
        foreach (var c in _tutBody.GetChildren()) { _tutBody.RemoveChild(c); c.QueueFree(); }
        _tutCard.ResetSize();
        int at = W.Tutorial;
        var hide = Ui.MakeButton("Hide", () => { W.Tutorial = -1; ShowTutorial(); }, Sfx);
        hide.CustomMinimumSize = new Vector2(0, 28);
        hide.FocusMode = FocusModeEnum.None;
        hide.TooltipText = "Hide the tutorial for this world";
        var title = at < Steps.Length ? $"Getting started  {at + 1}/{Steps.Length}" : "You've got it!";
        _tutBody.AddChild(Ui.Row(Ui.Grow(Ui.MakeLabel(title, 15, Ui.Accent)), hide));
        for (int i = 0; i < Math.Min(at, Steps.Length); i++)
            _tutBody.AddChild(Ui.MakeLabel("\u2713  " + Steps[i].Title, 13, Ui.Muted));
        if (at >= Steps.Length)
        {
            _tutBody.AddChild(Wrapped("That's the whole loop: mine, smelt, build, deliver. Assemblers, power and fluids unlock with milestones. Have fun!"));
            return;
        }
        var current = Ui.MakeLabel("\u25B6  " + Steps[at].Title, 15);
        _tutBody.AddChild(current);
        _tutBody.AddChild(Wrapped(Steps[at].Hint));
        if (advanced) Ui.PopIn(current);
    }

    static Label Wrapped(string text)
    {
        var l = Ui.MakeLabel(text, 13, Ui.Muted);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(280, 0);
        return l;
    }
}
