using System.IO;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Input;

namespace TerraAngel;

/// <summary>
/// Brings in what a player already has in their own TerraAngel, so the pack starts the way they left it:
/// - TerraAngel's settings (My Games/Terraria/TerraAngel/clientConfig.json), copied once; keys that would clash with
///   IsaClient's (F-keys, Z, Alt+I) and the packets that tell servers you use TerraAngel stay off;
/// - the Player Editor presets of TerraAngel forks (TerraAngel/PlayerPresets, playerEditor.json);
/// - the Item Editor's weapon setups (a WeaponConfigs folder next to TerraAngel's executable, found on the disk).
/// Presets and weapon setups are added on every start; files the player already has here are never overwritten.
/// </summary>
public static class TerraAngelImport
{
    public static string TerraAngelFolder => Path.Combine(Main.SavePath, "TerraAngel");

    public static string PresetsFolder => Path.Combine(ClientLoader.SavePath, "PlayerPresets");

    public static string WeaponConfigsFolder => Path.Combine(ClientLoader.SavePath, "WeaponConfigs");

    private static string Marker => Path.Combine(ClientLoader.DataPath, "imported-terraangel-config.txt");

    private static string InstallsFile => Path.Combine(ClientLoader.DataPath, "terraangel-installs.txt");

    /// <summary>What the last import did, for the tool's window.</summary>
    public static string Status { get; private set; } = "";

    public static bool Searching { get; private set; }

    public static List<string> Installs { get; } = new List<string>();

    public static bool HasTerraAngel => Directory.Exists(TerraAngelFolder);

    /// <summary>At the pack's start, before its settings are read.</summary>
    public static void OnLoad()
    {
        try
        {
            if (!File.Exists(Marker) && File.Exists(Path.Combine(TerraAngelFolder, "clientConfig.json")))
            {
                ImportConfig(apply: false);
            }
            LoadInstalls();
            int presets = ImportPresets();
            int weapons = ImportWeaponConfigs();
            if (presets + weapons > 0)
            {
                Status = $"Added {presets} preset(s) and {weapons} weapon setup(s) from TerraAngel.";
                IsaClient.Plugins.PluginLog.Info(Status);
            }
            if (Installs.Count == 0 && !File.Exists(InstallsFile))
            {
                FindInstalls();
            }
        }
        catch (Exception ex)
        {
            Status = "Import from TerraAngel failed: " + ex.Message;
            IsaClient.Plugins.PluginLog.Error(Status, ex);
        }
    }

    /// <summary>TerraAngel's settings replace the pack's (the clashing keys stay off).</summary>
    public static bool ImportConfig(bool apply)
    {
        string from = Path.Combine(TerraAngelFolder, "clientConfig.json");
        if (!File.Exists(from))
        {
            Status = "No TerraAngel settings found in " + TerraAngelFolder + ".";
            return false;
        }
        Directory.CreateDirectory(ClientLoader.DataPath);
        File.Copy(from, ClientLoader.ConfigPath, overwrite: true);
        ClientConfig.ReadFromFile();
        KeepClientSafe(ClientConfig.Settings);
        ClientConfig.WriteToFile();
        File.WriteAllText(Marker, "Imported " + from + " on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + Environment.NewLine);
        if (apply)
        {
            ClientConfig.AfterReadLater();
        }
        Status = "TerraAngel's settings were imported.";
        IsaClient.Plugins.PluginLog.Info(Status);
        return true;
    }

    /// <summary>What TerraAngel did with keys IsaClient uses, and what talks to servers behind the player's back.</summary>
    private static void KeepClientSafe(ClientConfig.Config settings)
    {
        foreach (Keys clash in new[] { Keys.F1, Keys.F2, Keys.F3, Keys.F4, Keys.F5, Keys.F6, Keys.F7, Keys.F8, Keys.F9, Keys.F10, Keys.F11, Keys.F12, Keys.Z, Keys.Insert })
        {
            foreach (System.Reflection.FieldInfo field in typeof(ClientConfig.Config).GetFields())
            {
                if (field.FieldType == typeof(Keys) && (Keys)field.GetValue(settings) == clash)
                {
                    field.SetValue(settings, Keys.None);
                }
            }
        }
        settings.OpenFastItemBrowser = Keys.None;
        settings.BroadcastPresence = false;
    }

    public static int ImportPresets()
    {
        int count = CopyMissing(Path.Combine(TerraAngelFolder, "PlayerPresets"), PresetsFolder);
        string editor = Path.Combine(TerraAngelFolder, "playerEditor.json");
        string mine = Path.Combine(ClientLoader.SavePath, "playerEditor.json");
        if (File.Exists(editor) && !File.Exists(mine))
        {
            File.Copy(editor, mine);
            count++;
        }
        return count;
    }

    public static int ImportWeaponConfigs()
    {
        int count = 0;
        foreach (string install in Installs.ToList())
        {
            count += CopyMissing(Path.Combine(install, "WeaponConfigs"), WeaponConfigsFolder);
        }
        // Where the pack kept them before (next to the game's executable).
        count += CopyMissing(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WeaponConfigs"), WeaponConfigsFolder);
        return count;
    }

    private static int CopyMissing(string from, string to)
    {
        if (!Directory.Exists(from))
        {
            return 0;
        }
        int count = 0;
        foreach (string file in Directory.GetFiles(from, "*.json"))
        {
            string target = Path.Combine(to, Path.GetFileName(file));
            if (File.Exists(target))
            {
                continue;
            }
            Directory.CreateDirectory(to);
            File.Copy(file, target);
            count++;
        }
        return count;
    }

    // ------------------------------------------------------------------ TerraAngel's executable folders

    private static void LoadInstalls()
    {
        Installs.Clear();
        if (File.Exists(InstallsFile))
        {
            Installs.AddRange(File.ReadAllLines(InstallsFile).Where(l => l.Length > 0 && Directory.Exists(l)));
        }
    }

    public static void AddInstall(string folder)
    {
        folder = folder.Trim().Trim('"');
        if (File.Exists(folder))
        {
            folder = Path.GetDirectoryName(folder)!;
        }
        if (!Directory.Exists(folder) || Installs.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }
        Installs.Add(folder);
        SaveInstalls();
        int count = ImportWeaponConfigs();
        Status = $"Added {folder}: {count} weapon setup(s).";
    }

    private static void SaveInstalls()
    {
        Directory.CreateDirectory(ClientLoader.DataPath);
        File.WriteAllLines(InstallsFile, Installs);
    }

    /// <summary>Looks for TerraAngel's executable in the usual places (desktop, downloads, documents, Steam), in the background.</summary>
    public static void FindInstalls()
    {
        if (Searching)
        {
            return;
        }
        Searching = true;
        Task.Run(() =>
        {
            List<string> found = new List<string>();
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string[] roots =
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Path.Combine(home, "Downloads"),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common"),
                    home
                };
                foreach (string root in roots.Distinct())
                {
                    Search(root, 4, found);
                }
            }
            catch
            {
            }
            IsaClient.Plugins.PluginHost.BeforeNextFrame(() =>
            {
                foreach (string folder in found.Where(f => !Installs.Contains(f, StringComparer.OrdinalIgnoreCase)))
                {
                    Installs.Add(folder);
                }
                SaveInstalls();
                int count = ImportWeaponConfigs();
                Status = found.Count == 0 ? "TerraAngel's executable was not found; add its folder by hand." : $"Found TerraAngel in {found.Count} folder(s), {count} weapon setup(s) added.";
                Searching = false;
            });
        });
    }

    private static readonly HashSet<string> SkippedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AppData", "node_modules", ".git", "Windows", "$Recycle.Bin", "obj", "Content", "Libraries" };

    private static void Search(string folder, int depth, List<string> found)
    {
        if (depth < 0 || !Directory.Exists(folder) || found.Count >= 10)
        {
            return;
        }
        try
        {
            if (File.Exists(Path.Combine(folder, "TerraAngel.exe")) || File.Exists(Path.Combine(folder, "TerraAngel.dll")) || Directory.Exists(Path.Combine(folder, "WeaponConfigs")))
            {
                if (!string.Equals(folder.TrimEnd('\\'), AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) && !found.Contains(folder))
                {
                    found.Add(folder);
                }
            }
            foreach (string sub in Directory.GetDirectories(folder))
            {
                if (!SkippedFolders.Contains(Path.GetFileName(sub)) && (new DirectoryInfo(sub).Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    Search(sub, depth - 1, found);
                }
            }
        }
        catch
        {
        }
    }
}
