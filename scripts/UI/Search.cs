using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TinyWorks.UI;

// Search field and "Recently used" at the top of a list panel (recipes, blueprints, items, builds).
public sealed partial class Hud
{
    // Adds the search field and the recent entries to body, then returns the function the caller uses to register every list
    // entry it adds afterwards: (control, name, the group heading above it or null). Typing only hides entries, so nothing jumps.
    // make(key) builds one entry; it also builds the "Recently used" copies. valid = the keys that exist now. cols 0 = one per row.
    Action<Control, string, Control> SearchBox(VBoxContainer body, string list, ICollection<string> valid, Func<string, Control> make, int cols = 0)
    {
        var entries = new List<(Control C, string Name, Control Head)>();
        var search = new LineEdit { PlaceholderText = "Search", ClearButtonEnabled = true, MaxLength = 40, TooltipText = "Type part of a name" };
        body.AddChild(search);

        var recent = new VBoxContainer();
        recent.AddThemeConstantOverride("separation", 6);
        recent.AddChild(Ui.MakeLabel("Recently used", 13, Ui.Muted));
        Container holder = cols > 0 ? new GridContainer { Columns = cols } : new VBoxContainer();
        holder.AddThemeConstantOverride("h_separation", 6);
        holder.AddThemeConstantOverride("v_separation", 6);
        recent.AddChild(holder);
        foreach (var key in W.Recent.GetValueOrDefault(list, new()).Where(valid.Contains)) holder.AddChild(make(key));
        recent.Visible = holder.GetChildCount() > 0;
        bool hasRecent = recent.Visible;
        body.AddChild(recent);

        var none = Ui.MakeLabel("Nothing matches.", 13, Ui.Muted);
        none.Visible = false;
        body.AddChild(none);

        search.TextChanged += text =>
        {
            var q = text.Trim();
            foreach (var (c, name, _) in entries) c.Visible = q.Length == 0 || name.Contains(q, StringComparison.OrdinalIgnoreCase);
            foreach (var head in entries.Select(e => e.Head).Where(h => h != null).Distinct()) head.Visible = entries.Any(e => e.Head == head && e.C.Visible);
            recent.Visible = hasRecent && q.Length == 0;
            none.Visible = q.Length > 0 && !entries.Any(e => e.C.Visible);
        };
        return (c, name, head) => entries.Add((c, name, head));
    }
}
