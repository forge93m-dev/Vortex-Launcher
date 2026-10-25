using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using VortexLauncher.Core.Launch;

namespace VortexLauncher.Views;

/// <summary>
/// The review step before a pack is imported: what the pack is, what it contains, what the launcher will fetch
/// itself and what is left to the player. Nothing is created until "Import instance" is clicked.
/// </summary>
public partial class ImportPreviewWindow : Window
{
    public enum Level { Good, Warning, Info }
    public sealed record Row(Level Level, string Title, string Detail, bool Blocks = false);

    private static readonly IBrush Good = new SolidColorBrush(Color.Parse("#6EE7A8"));
    private static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#F0B44C"));
    private static readonly IBrush Info = new SolidColorBrush(Color.Parse("#8BB4F5"));
    /// <summary>Unpacked files plus this much headroom must fit on the disk, or importing is blocked.</summary>
    private const double SpaceMargin = 1.2;

    public ImportPreviewWindow() : this(new ArchivePreview("", "Pack", "Pack", "", "Vanilla", "", 0, 0, 0, 0, 0, 0, 0), true, null) { }

    public ImportPreviewWindow(ArchivePreview preview, bool curseForgeKeySet, long? freeBytes)
    {
        InitializeComponent();
        WindowTitleBar.Apply(this);
        UiMotion.AttachDialog(this, dialogContent);
        string name = string.IsNullOrWhiteSpace(preview.Name) ? "Imported pack" : preview.Name.Trim();
        PackName.Text = name;
        char first = name.FirstOrDefault(char.IsLetterOrDigit);
        LogoLetter.Text = char.ToUpperInvariant(first == default ? 'P' : first).ToString();
        foreach (string chip in ChipLabels(preview)) Chips.Children.Add(new Border { Classes = { "ImportChip" }, Child = new TextBlock { Text = chip, FontSize = 12 } });
        foreach (var (value, label) in StatTiles(preview)) Stats.Children.Add(Stat(value, label));
        var rows = BuildRows(preview, curseForgeKeySet, freeBytes);
        foreach (var row in rows) Rows.Children.Add(RowView(row));
        long needed = Required(preview);
        DiskText.Text = freeBytes is long free
            ? $"Free disk space: {Size(free)} · needed: about {Size(needed)}"
            : $"Needed disk space: about {Size(needed)}";
        SizeText.Text = preview.Downloads > 0 ? $"{Num(preview.Downloads)} files to download" : "no downloads";
        Blocked = rows.Any(r => r.Blocks);
        ImportButton.IsEnabled = !Blocked;
    }

    /// <summary>True when the review found something that stops the import (no key, or not enough disk space).</summary>
    public bool Blocked { get; }

    internal static long Required(ArchivePreview preview) => (long)(Math.Max(0, preview.Bytes) * SpaceMargin);

    private static string Num(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    internal static string Size(long bytes) => bytes >= 1L << 30 ? (bytes / 1073741824d).ToString("N1", CultureInfo.InvariantCulture) + " GB" : bytes >= 1L << 20 ? Num((long)Math.Round(bytes / 1048576d)) + " MB" : Num((long)Math.Round(Math.Max(0, bytes) / 1024d)) + " KB";

    internal static IReadOnlyList<string> ChipLabels(ArchivePreview preview)
    {
        var chips = new List<string>();
        if (!string.IsNullOrWhiteSpace(preview.Format)) chips.Add(preview.Format);
        if (!string.IsNullOrWhiteSpace(preview.Version)) chips.Add("Minecraft " + preview.Version);
        string loader = preview.Loader == "Vanilla" ? "Vanilla" : (preview.Loader + " " + preview.LoaderVersion).Trim();
        if (loader.Length > 0) chips.Add(loader);
        return chips;
    }

    internal static IReadOnlyList<(string Value, string Label)> StatTiles(ArchivePreview preview) => new[]
    {
        (Num(preview.Mods), "mods"), (Num(preview.ResourcePacks), "resource packs"),
        (Num(preview.ShaderPacks), "shader packs"), (Num(preview.Worlds), preview.Worlds == 1 ? "world" : "worlds")
    };

    /// <summary>What the player needs to know, most important first. A warning that blocks the import says so in its title.</summary>
    internal static IReadOnlyList<Row> BuildRows(ArchivePreview preview, bool curseForgeKeySet, long? freeBytes)
    {
        var rows = new List<Row>();
        bool curseForge = string.Equals(preview.Format, "CurseForge", StringComparison.OrdinalIgnoreCase);
        if (curseForge)
        {
            rows.Add(curseForgeKeySet
                ? new Row(Level.Good, "Your CurseForge key is set", "It stays on this computer and is only used to look up the pack's files.")
                : new Row(Level.Warning, "CurseForge key needed", "Enter your free key in Settings → Java → CurseForge, then import again.", Blocks: true));
            rows.Add(new Row(Level.Good, $"The launcher fetches {Num(preview.Downloads)} files itself",
                "Each one is checked against its published checksum before it is used."));
            rows.Add(new Row(Level.Warning, "Some files may need a manual download",
                "Authors can block other launchers. Those files are listed in a text file in the instance folder."));
        }
        else if (preview.Downloads > 0)
            rows.Add(new Row(Level.Good, $"The launcher fetches {Num(preview.Downloads)} files itself", "Each one is checked against its published checksum before it is used."));
        else
            rows.Add(new Row(Level.Good, "Everything is inside the pack", "No downloads are needed."));
        if (preview.Worlds > 0)
            rows.Add(new Row(Level.Info, preview.Worlds == 1 ? "The pack includes a world" : $"The pack includes {Num(preview.Worlds)} worlds", "It is copied into the new instance only."));
        if (freeBytes is long free && free < Required(preview))
            rows.Add(new Row(Level.Warning, "Not enough disk space", $"The import needs about {Size(Required(preview))}; {Size(Math.Max(0, free))} is free. Free some space or pick another drive in Settings.", Blocks: true));
        return rows;
    }

    private static Control Stat(string value, string label)
    {
        var text = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Spacing = 1 };
        text.Children.Add(new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        var caption = new TextBlock { Text = label, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
        caption.Classes.Add("Muted"); text.Children.Add(caption);
        var card = new Border { Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 10), Child = text };
        card.Classes.Add("ImportCard");
        return card;
    }

    private static Control RowView(Row row)
    {
        var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 5, 12, 0), VerticalAlignment = VerticalAlignment.Top,
            Background = row.Level switch { Level.Good => Good, Level.Warning => Warning, _ => Info } };
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = row.Title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        var detail = new TextBlock { Text = row.Detail, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        detail.Classes.Add("Muted"); text.Children.Add(detail);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(dot); Grid.SetColumn(text, 1); grid.Children.Add(text);
        var card = new Border { Padding = new Thickness(14, 10), Child = grid };
        card.Classes.Add("ImportCard");
        return card;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
    private void OnImportClick(object? sender, RoutedEventArgs e) => Close(!Blocked);

    public static Task<bool> ConfirmAsync(ArchivePreview preview, bool curseForgeKeySet, long? freeBytes) =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner }
            ? new ImportPreviewWindow(preview, curseForgeKeySet, freeBytes).ShowDialog<bool>(
                (Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.LastOrDefault(w => w.IsActive) ?? owner)
            : Task.FromResult(false);
}
