using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using TinyWorks.Sim;

namespace TinyWorks;

public sealed class Settings
{
    public string LastSlot;
    public bool SkipTitle;
    public string WindowMode = "Normal";
    public int Corner = 3; // bottom-right
    public float MiniOpacity = 0.85f;     // while the mouse is over the mini window
    public float MiniIdleOpacity = 0.5f;  // while it isn't
    public float MiniHoverScale = 1.25f;  // hovered size = mini size times this
    public Dictionary<string, string> LastTier = new(); // per tier family (by base id): the tier its slot starts with
    public int MiniW = 400, MiniH = 300;
    public int NormalX = -1, NormalY = -1, NormalW = 1280, NormalH = 720;
    public int FpsFocused = 60, FpsBackground = 60; // 0 = no limit
    public bool VSync = true;
    public string HideKey = "F8", ModeKey = "F7"; // Godot key names, e.g. "Ctrl+Shift+F8"
    public float SfxVolume = 0.8f, MusicVolume = 0.6f;
    public string MusicFolder; // null = the built-in music
    public int ScareHits = 1, ScareOutOf = 500; // a jumpscare's chance every second: ScareHits in ScareOutOf; 0 = never
    public bool ScareUnlocked; // off until the title logo is clicked 10 times
    public float ScareVolume = 0.8f;
    public Dictionary<string, float> PotionStrength = new(); // per potion id: the strength last picked
    // Juice families: place, production, screen, ui. Each can be off in normal or mini mode.
    public Dictionary<string, bool> Juice = new();

    public bool JuiceOn(string family, bool mini) => Juice.GetValueOrDefault($"{family}.{(mini ? "mini" : "normal")}", true);

    // HUD info that can be switched off: storage, coins, milestone, bottleneck, hints.
    public Dictionary<string, bool> Stats = new();
    public bool StatOn(string key) => Stats.GetValueOrDefault(key, true);
}

// Everything on disk: settings and save slots under user:// (a TinyWorks folder in the user data dir).
public static class Persist
{
    static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    // Overridable with "-- --data <dir>" so tests never touch the player's real saves.
    public static string Root = ProjectSettings.GlobalizePath("user://");
    static string SavesDir => Path.Combine(Root, "saves");
    static string SettingsPath => Path.Combine(Root, "settings.json");

    public static Settings LoadSettings()
    {
        try
        {
            var s = File.Exists(SettingsPath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath), Json) ?? new() : new();
            if (s.ScareOutOf == 10000) s.ScareOutOf = 500; // the old default; someone who typed 10000 themselves gets 500 once
            s.PotionStrength ??= new();
            return s;
        }
        catch (Exception e) { GD.PushWarning($"Settings unreadable, using defaults: {e.Message}"); return new(); }
    }

    public static void SaveSettings(Settings s) => WriteAtomic(SettingsPath, JsonSerializer.Serialize(s, Json));

    // Blueprints are shared by every save. Parts of buildings this version doesn't know are dropped on load.
    static string BlueprintsPath => Path.Combine(Root, "blueprints.json");

    public static SortedDictionary<string, List<BpPart>> LoadBlueprints()
    {
        try
        {
            var all = File.Exists(BlueprintsPath) ? JsonSerializer.Deserialize<SortedDictionary<string, List<BpPart>>>(File.ReadAllText(BlueprintsPath), Json) ?? new() : new();
            foreach (var parts in all.Values) parts.RemoveAll(p => p?.Type == null || !Defs.Buildings.ContainsKey(p.Type) || p.Type == "hub");
            return all;
        }
        catch (Exception e) { GD.PushWarning($"Blueprints unreadable: {e.Message}"); return new(); }
    }

    public static void SaveBlueprints(SortedDictionary<string, List<BpPart>> all) => WriteAtomic(BlueprintsPath, JsonSerializer.Serialize(all, Json));

    // Checks the save before it touches the disk: it must read back into a world with the same buildings.
    // A save that fails the check is not written, so the file from the last good save stays.
    public static void SaveSlot(string slot, SaveData data)
    {
        var text = JsonSerializer.Serialize(data, Json);
        var back = World.FromSave(Deserialize(text) ?? throw new IOException("the save came out empty"));
        if (back.Entities.Count != data.Entities.Count) throw new IOException("the save doesn't read back the same");
        var path = Path.Combine(SavesDir, slot + ".json");
        WriteAtomic(path, text);
        if (File.ReadAllText(path) != text) throw new IOException("the file on disk doesn't match the game");
    }

    public static bool LoadedBackup; // set by LoadSlot when the main file was broken and the backup was used

    // Falls back to the previous save (slot.json.bak) when the file is empty, cut off or unreadable.
    public static SaveData LoadSlot(string slot)
    {
        var path = Path.Combine(SavesDir, slot + ".json");
        LoadedBackup = false;
        try { return Deserialize(File.ReadAllText(path)) ?? throw new IOException("the save file is empty"); }
        catch (Exception e) when (File.Exists(path + ".bak"))
        {
            GD.PushWarning($"Save {slot} unreadable ({e.Message}), loading its backup");
            var data = Deserialize(File.ReadAllText(path + ".bak")) ?? throw new IOException("the save and its backup are empty");
            LoadedBackup = true;
            return data;
        }
    }

    public static bool SlotExists(string slot) => slot != null && File.Exists(Path.Combine(SavesDir, slot + ".json"));

    public static void DeleteSlot(string slot)
    {
        File.Delete(Path.Combine(SavesDir, slot + ".json"));
        File.Delete(Path.Combine(SavesDir, slot + ".json.bak"));
    }

    public static string NewSlotId() => DateTime.Now.ToString("yyyyMMdd-HHmmss");

    // Newest first. Unreadable files are listed with a note instead of breaking the menu.
    public static List<(string Slot, SaveData Data)> ListSlots()
    {
        var list = new List<(string, SaveData)>();
        if (!Directory.Exists(SavesDir)) return list;
        foreach (var f in Directory.GetFiles(SavesDir, "*.json"))
        {
            var slot = Path.GetFileNameWithoutExtension(f);
            try { list.Add((slot, LoadSlot(slot))); }
            catch { list.Add((slot, new SaveData { Name = slot + " (unreadable)" })); }
        }
        list.Sort((a, b) => string.CompareOrdinal(b.Item1, a.Item1));
        return list;
    }

    // Write a temp file, force it onto the disk, then swap it in and keep the old file as .bak.
    // Without the flush, a power cut or hard crash can leave a renamed file that is empty or all zero bytes.
    static void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = $"{path}.{System.Environment.ProcessId}.tmp"; // per process, so two running games never share one temp file
        using (var f = new FileStream(tmp, FileMode.Create, System.IO.FileAccess.Write, FileShare.None))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            f.Write(bytes);
            f.Flush(flushToDisk: true);
        }
        if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
        else File.Move(tmp, path);
    }

    public static string Serialize(SaveData s) => JsonSerializer.Serialize(s, Json);
    public static SaveData Deserialize(string s) => JsonSerializer.Deserialize<SaveData>(s, Json);
}
