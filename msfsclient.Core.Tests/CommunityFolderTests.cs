using msfsclient.Core.Hud;

namespace msfsclient.Core.Tests;

public sealed class CommunityFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"airsports-community-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void ReadsThePackagesFolderFromUserCfg()
    {
        const string cfg = "Version 66\r\n{Graphics\r\n}\r\nInstalledPackagesPath \"D:\\MSFS Packages\"\r\nLastVersion 1.37\r\n";
        Assert.Equal(@"D:\MSFS Packages", CommunityFolders.InstalledPackagesPath(cfg));
        Assert.Null(CommunityFolders.InstalledPackagesPath("Version 66\n"));
        Assert.Null(CommunityFolders.InstalledPackagesPath("InstalledPackagesPath \"\"\n"));
    }

    [Fact]
    public void FindsEachInstalledSimulatorWithAnExistingCommunityFolder()
    {
        var local = Path.Combine(_root, "Local");
        var roaming = Path.Combine(_root, "Roaming");
        var store2020 = Path.Combine(_root, "Store2020Packages");
        var steam2024 = Path.Combine(_root, "Steam2024Packages");
        Directory.CreateDirectory(Path.Combine(store2020, "Community"));
        Directory.CreateDirectory(Path.Combine(steam2024, "Community"));
        WriteCfg(CommunityFolders.Candidates(local, roaming)[0].UserCfg, store2020);
        WriteCfg(CommunityFolders.Candidates(local, roaming)[3].UserCfg, steam2024);
        // A config pointing at a folder without Community is not offered.
        WriteCfg(CommunityFolders.Candidates(local, roaming)[1].UserCfg, Path.Combine(_root, "Nowhere"));

        var found = CommunityFolders.Find(local, roaming);
        Assert.Equal(["MSFS 2020 (Microsoft Store)", "MSFS 2024 (Steam)"], found.Select(f => f.Name));
        Assert.Equal(Path.Combine(store2020, "Community"), found[0].CommunityFolder);
    }

    [Fact]
    public void InstallsReplacesAndUninstallsThePanel()
    {
        var package = Path.Combine(_root, "dist", PanelInstaller.PackageName);
        Directory.CreateDirectory(Path.Combine(package, "html_ui"));
        File.WriteAllText(Path.Combine(package, "layout.json"), "{}");
        File.WriteAllText(Path.Combine(package, "manifest.json"), """{"package_version":"1.0.0"}""");
        File.WriteAllText(Path.Combine(package, "html_ui", "a.txt"), "new");
        var community = Path.Combine(_root, "Community");
        Directory.CreateDirectory(Path.Combine(community, PanelInstaller.PackageName));
        File.WriteAllText(Path.Combine(community, PanelInstaller.PackageName, "stale.txt"), "old");

        Assert.Equal("", PanelInstaller.InstalledVersion(community)); // an old copy without a manifest
        PanelInstaller.Install(package, community);

        var installed = Path.Combine(community, PanelInstaller.PackageName);
        Assert.Equal("new", File.ReadAllText(Path.Combine(installed, "html_ui", "a.txt")));
        Assert.False(File.Exists(Path.Combine(installed, "stale.txt")));
        Assert.Equal("1.0.0", PanelInstaller.InstalledVersion(community));
        Assert.Equal([PanelInstaller.PackageName], Directory.GetDirectories(community).Select(Path.GetFileName));

        PanelInstaller.Uninstall(community);
        Assert.Null(PanelInstaller.InstalledVersion(community));
    }

    [Fact]
    public void AnIncompletePackageIsNotInstalled()
    {
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);
        Assert.Throws<InvalidOperationException>(() => PanelInstaller.Install(empty, _root));
    }

    private static void WriteCfg(string path, string packages)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"Version 66\r\nInstalledPackagesPath \"{packages}\"\r\n");
    }
}
