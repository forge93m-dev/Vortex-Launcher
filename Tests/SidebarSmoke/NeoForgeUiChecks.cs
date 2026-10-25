using VortexLauncher.Core.Network;
using VortexLauncher.ViewModels;

// NeoForge is offered like the other loaders: no "not supported" label in the modpack filter, and it counts as part of the Forge family.
internal static class NeoForgeUiChecks
{
    internal static void Run()
    {
        var modpacks = new ModpacksViewModel(_ => Task.CompletedTask, (_, _, _) => { });
        var neo = modpacks.Loaders.SingleOrDefault(l => l.Id == "neoforge");
        Check(neo != null && neo.Label == "NeoForge", "the modpack search offers NeoForge as a normal loader");
        Check(ForgeService.IsForgeFamily("Forge") && ForgeService.IsForgeFamily("neoforge") && !ForgeService.IsForgeFamily("Fabric") && !ForgeService.IsForgeFamily(null),
            "Forge and NeoForge share the install and launch path; other loaders do not");
        Console.WriteLine("NEOFORGE_UI_OK");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
}
