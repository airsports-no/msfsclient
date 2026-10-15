namespace msfsclient.Core.Hud;

/// <summary>An installed simulator and its Community folder (where add-on packages go).</summary>
public sealed record SimInstall(string Name, string CommunityFolder);

/// <summary>
/// Finds the Community folders of MSFS 2020 and 2024 (Microsoft Store and Steam) from each install's UserCfg.opt, whose
/// <c>InstalledPackagesPath "D:\MSFS"</c> line says where the packages are; the Community folder is inside it.
/// </summary>
public static class CommunityFolders
{
    /// <summary>Where each simulator keeps UserCfg.opt.</summary>
    public static IReadOnlyList<(string Name, string UserCfg)> Candidates(string localAppData, string roamingAppData) =>
    [
        ("MSFS 2020 (Microsoft Store)", Path.Combine(localAppData, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt")),
        ("MSFS 2020 (Steam)", Path.Combine(roamingAppData, "Microsoft Flight Simulator", "UserCfg.opt")),
        ("MSFS 2024 (Microsoft Store)", Path.Combine(localAppData, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt")),
        ("MSFS 2024 (Steam)", Path.Combine(roamingAppData, "Microsoft Flight Simulator 2024", "UserCfg.opt")),
    ];

    /// <summary>The packages folder named in a UserCfg.opt, or null when it has none.</summary>
    public static string? InstalledPackagesPath(string userCfg)
    {
        foreach (var raw in userCfg.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("InstalledPackagesPath", StringComparison.OrdinalIgnoreCase)) continue;
            var value = line["InstalledPackagesPath".Length..].Trim().Trim('"').Trim();
            return value.Length == 0 ? null : value;
        }
        return null;
    }

    /// <summary>The simulators installed for this user whose Community folder exists.</summary>
    public static IReadOnlyList<SimInstall> Find(string localAppData, string roamingAppData)
    {
        var found = new List<SimInstall>();
        foreach (var (name, userCfg) in Candidates(localAppData, roamingAppData))
        {
            try
            {
                if (!File.Exists(userCfg)) continue;
                var packages = InstalledPackagesPath(File.ReadAllText(userCfg));
                if (packages == null) continue;
                var community = Path.Combine(packages, "Community");
                if (Directory.Exists(community) && !found.Any(f => string.Equals(f.CommunityFolder, community, StringComparison.OrdinalIgnoreCase)))
                {
                    found.Add(new SimInstall(name, community));
                }
            }
            catch (Exception)
            {
                // An unreadable config is not a simulator we can install into.
            }
        }
        return found;
    }

    public static IReadOnlyList<SimInstall> FindForCurrentUser() => Find(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
}

/// <summary>Copies the toolbar panel package into a Community folder, replacing an older copy.</summary>
public static class PanelInstaller
{
    public const string PackageName = "airsports-flight-display";

    /// <summary>The version of the panel installed in <paramref name="communityFolder"/> (its manifest), or null when there is none.</summary>
    public static string? InstalledVersion(string communityFolder)
    {
        var folder = Path.Combine(communityFolder, PackageName);
        if (!Directory.Exists(folder)) return null;
        var manifest = Path.Combine(folder, "manifest.json");
        if (!File.Exists(manifest)) return ""; // installed, version unknown
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest));
            return doc.RootElement.TryGetProperty("package_version", out var v) ? v.GetString() : "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>
    /// Installs <paramref name="packageFolder"/> (the assembled package) as Community\airsports-flight-display. The copy is
    /// made beside it first and swapped in at the end, so a failed copy never leaves a half package for the simulator.
    /// </summary>
    public static void Install(string packageFolder, string communityFolder)
    {
        if (!File.Exists(Path.Combine(packageFolder, "layout.json"))) throw new InvalidOperationException("The panel package is missing or incomplete.");
        var target = Path.Combine(communityFolder, PackageName);
        var staging = target + ".installing";
        var old = target + ".old";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        CopyDirectory(packageFolder, staging);
        if (Directory.Exists(old)) Directory.Delete(old, true);
        if (Directory.Exists(target)) Directory.Move(target, old);
        Directory.Move(staging, target);
        if (Directory.Exists(old)) Directory.Delete(old, true);
    }

    public static void Uninstall(string communityFolder)
    {
        var target = Path.Combine(communityFolder, PackageName);
        if (Directory.Exists(target)) Directory.Delete(target, true);
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(from)) CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
