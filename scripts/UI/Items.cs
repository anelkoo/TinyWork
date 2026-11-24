using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TinyWorks.Sim;
using TinyWorks.View;

namespace TinyWorks.UI;

// Item book (I): every item in the game; click one for its price, where it comes from and what uses it.
public sealed partial class Hud
{
    public void OpenItems()
    {
        var body = OpenPanel("Items", 660);
        string group = null;
        GridContainer grid = null;
        foreach (var id in Defs.Items.Keys.OrderBy(Defs.GroupIndex))
        {
            if (Defs.GroupOf(id) != group)
            {
                GroupHead(body, ref group, id);
                grid = new GridContainer { Columns = _mini ? 2 : 3 };
                grid.AddThemeConstantOverride("h_separation", 6);
                grid.AddThemeConstantOverride("v_separation", 6);
                body.AddChild(grid);
            }
            var b = new Button { Text = Defs.Items[id].Name, Icon = Sprites.Items[id], ExpandIcon = true, CustomMinimumSize = new Vector2(_mini ? 150 : 206, 40), Alignment = HorizontalAlignment.Left, ClipText = true, FocusMode = FocusModeEnum.None };
            b.AddThemeConstantOverride("icon_max_width", 22);
            b.Pressed += () => { Sfx.Play("click"); OpenItem(id); };
            Ui.Squishy(b);
            grid.AddChild(b);
        }
    }

    public void OpenItem(string id)
    {
        var def = Defs.Items[id];
        string N(string x) => Defs.Items[x].Name;
        string Ins((string Item, int Count)[] list) => string.Join(" + ", list.Select(x => $"{x.Count} {N(x.Item)}"));
        string Milestone(string unlock) => unlock == null ? "" : unlock.StartsWith("research_") ? "  (research)" : $"  (milestone {Defs.UnlockTier(unlock)})";

        Label stored = null;
        var back = Ui.MakeButton("All items", OpenItems, Sfx);
        back.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        var body = OpenPanel(def.Name, 560, () => stored.Text = def.Fluid ? "" : $"In storage: {Compact(W.Storage.GetValueOrDefault(id))}" + (W.AutoSell.Contains(id) ? ", auto-sell on" : ""), back);
        var title = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddChild(Ui.MakeLabel(def.Name, 18));
        title.AddChild(Ui.MakeLabel(Defs.GroupOf(id), 13, Ui.Muted));
        stored = Ui.MakeLabel("", 13, Ui.Muted);
        title.AddChild(stored);
        body.AddChild(Ui.Row(Ui.Icon(Sprites.Items[id], 40), title));
        body.AddChild(Ui.MakeLabel(def.Fluid ? "A fluid: it lives in pipes and can't be sold." : $"Sells for {def.Price} coins at the Hub", 15, new Color("f7c948")));

        void Section(string head, string[] lines)
        {
            body.AddChild(Ui.MakeLabel(head, 16));
            foreach (var line in lines.Length > 0 ? lines : new[] { "Nothing" })
            {
                var l = Ui.MakeLabel(line, 14, lines.Length > 0 ? Ui.Text : Ui.Muted);
                l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                body.AddChild(l);
            }
        }

        var get = Defs.Recipes.Where(r => r.Out == id)
            .Select(r => $"{Defs.Buildings[r.Machine].Name}: {Ins(r.In)} makes {r.OutCount}{Milestone(r.Unlock)}").ToList();
        if (Array.IndexOf(Defs.Ores, id) >= 0)
        {
            get.Insert(0, "Put a miner on its ore patch, or click the ore to mine by hand");
            get.Add($"Buy {World.RawBatch} in the Hub's Shop for {World.RawCost(id)} coins");
        }
        if (id == "water") get.Insert(0, "A pump pushes it into the pipe in front of it");
        Section("How to get it", get.ToArray());

        var use = Defs.Recipes.Where(r => r.In.Any(x => x.Item == id))
            .Select(r => $"{Defs.Buildings[r.Machine].Name}: {Ins(r.In)} makes {r.OutCount} {N(r.Out)}{Milestone(r.Unlock)}").ToList();
        if (id is "coal" or "fuel" || Defs.GasKJ.ContainsKey(id)) use.Add("Generators burn it for power");
        Section("Used in", use.ToArray());

        Section("Buildings that cost it", Defs.Buildings.Values.Where(b => b.Cost.Any(c => c.Item == id))
            .Select(b => $"{b.Name}: {b.Cost.First(c => c.Item == id).Count}").ToArray());
        Section("Milestones that need it", Defs.Milestones.Select((m, i) => (m, i)).Where(x => x.m.Need.Any(c => c.Item == id))
            .Select(x => $"{x.i + 1}. {x.m.Name}: {x.m.Need.First(c => c.Item == id).Count}").ToArray());
    }
}
