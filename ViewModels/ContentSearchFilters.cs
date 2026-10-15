using System.Collections.Generic;

namespace VortexLauncher.ViewModels;

internal static class ContentSearchFilters
{
    public static IReadOnlyList<ModSearchFilter> For(string projectType) => projectType switch
    {
        "modpack" => Create(
            ("adventure", "Adventure"), ("challenging", "Challenging"), ("combat", "Combat"),
            ("kitchen-sink", "Kitchen sink / all-in-one"), ("lightweight", "Lightweight"),
            ("magic", "Magic"), ("multiplayer", "Multiplayer"), ("optimization", "Optimization / FPS"),
            ("quests", "Quests"), ("technology", "Technology")),
        "resourcepack" => Create(
            ("vanilla-like", "Vanilla-like"), ("realistic", "Realistic"), ("simplistic", "Simplistic"),
            ("themed", "Themed"), ("tweaks", "Tweaks"), ("utility", "Utilities"), ("modded", "Modded"),
            ("audio", "Sounds / music"), ("blocks", "Blocks"), ("entities", "Entities"), ("items", "Items"),
            ("equipment", "Equipment"), ("environment", "Environment"), ("gui", "Interface"),
            ("fonts", "Fonts"), ("locale", "Translations"), ("models", "Models"),
            ("decoration", "Decoration"), ("combat", "Combat"), ("core-shaders", "Core shaders"),
            ("cursed", "Unusual / cursed"), ("8x-", "8x or lower"), ("16x", "16x resolution"),
            ("32x", "32x resolution"), ("48x", "48x resolution"), ("64x", "64x resolution"),
            ("128x", "128x resolution"), ("256x", "256x resolution"), ("512x+", "512x or higher")),
        "shader" => Create(
            ("potato", "Very low-end hardware"), ("low", "Low requirements"), ("medium", "Medium requirements"),
            ("high", "High requirements"), ("vanilla-like", "Vanilla-like"), ("realistic", "Realistic"),
            ("semi-realistic", "Semi-realistic"), ("cartoon", "Cartoon"), ("fantasy", "Fantasy"),
            ("atmosphere", "Atmosphere"), ("bloom", "Bloom"), ("colored-lighting", "Colored lighting"),
            ("foliage", "Foliage"), ("path-tracing", "Path tracing"), ("pbr", "PBR textures"),
            ("reflections", "Reflections"), ("shadows", "Shadows"), ("screenshot", "Screenshots / cinematic"),
            ("cursed", "Unusual / cursed")),
        _ => Create(
            ("optimization", "Optimization / FPS"), ("utility", "Utilities / interface"),
            ("adventure", "Adventure"), ("technology", "Technology"), ("magic", "Magic"),
            ("worldgen", "World generation"), ("mobs", "Mobs"), ("decoration", "Decoration / building"),
            ("equipment", "Weapons / equipment"), ("storage", "Storage"), ("food", "Food"),
            ("transportation", "Transportation"), ("game-mechanics", "Game mechanics"),
            ("economy", "Economy"), ("social", "Social / multiplayer"), ("management", "Management"),
            ("minigame", "Minigames"), ("library", "Libraries / dependencies"), ("cursed", "Unusual / cursed"))
    };

    public static IReadOnlyList<ModSearchFilter> SortOptions { get; } = new ModSearchFilter[]
    {
        new("relevance", "Relevance"), new("downloads", "Most downloaded"),
        new("follows", "Most followed"), new("newest", "Newest first"), new("updated", "Recently updated")
    };

    private static IReadOnlyList<ModSearchFilter> Create(params (string Id, string Label)[] entries)
    {
        var filters = new List<ModSearchFilter> { new("", "All categories") };
        foreach (var entry in entries) filters.Add(new(entry.Id, entry.Label));
        return filters;
    }
}
