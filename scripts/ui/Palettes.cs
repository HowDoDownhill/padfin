using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Padfin;

/// <summary>Colour palettes and animated backgrounds, the same set RomBox ships.</summary>
public static class Palettes
{
    public const string DefaultName = "Dark";

    public static readonly (string Name, Color Bg, Color Primary, Color Secondary, Color Panel)[] All =
    {
        ("Default", new Color("#0a0713"), new Color("#341052"), new Color("#123a63"), new Color("#100c1a8c")),
        ("Light", new Color("#ebecf0"), new Color("#cfd8ea"), new Color("#2f6fd6"), new Color("#ffffffb3")),
        ("Dark", new Color("#2b2c30"), new Color("#3b3d44"), new Color("#2f6fd6"), new Color("#34363c8c")),
        ("Midnight", new Color("#04060d"), new Color("#13215a"), new Color("#0a4a6e"), new Color("#080c1a8c")),
        ("Obsidian", new Color("#0a0908"), new Color("#523312"), new Color("#7a5f22"), new Color("#13110e8c")),
        ("Amethyst", new Color("#0b0612"), new Color("#3a1a66"), new Color("#6e1c52"), new Color("#140c1f8c")),
        ("Glacier", new Color("#070c12"), new Color("#1d4159"), new Color("#4a7186"), new Color("#0d151d8c")),
        ("Bordeaux", new Color("#0d0508"), new Color("#4c0e22"), new Color("#7c3f23"), new Color("#170b0f8c")),
        ("Jade", new Color("#040c0a"), new Color("#0b3d32"), new Color("#3f6230"), new Color("#0a16138c")),
        ("Graphite", new Color("#0b0c0f"), new Color("#22262e"), new Color("#3a5577"), new Color("#14161b8c")),
        ("Rose-pine", new Color("#16141f"), new Color("#4a3663"), new Color("#8a3f59"), new Color("#1f1d2e8c")),
        ("Gruvbox", new Color("#1d2021"), new Color("#7a2f24"), new Color("#2f5f57"), new Color("#2828288c")),
        ("catppuccin", new Color("#181825"), new Color("#4c3a6b"), new Color("#33517f"), new Color("#1e1e2e8c")),
        ("Solarized Dark", new Color("#00212b"), new Color("#6b3410"), new Color("#10537f"), new Color("#0736428c")),
        ("Solarized Light", new Color("#3b3630"), new Color("#6e3a55"), new Color("#2f7a70"), new Color("#4a443a8c")),
        ("monokai", new Color("#1e1f1c"), new Color("#7a1436"), new Color("#1f6b7a"), new Color("#2728228c")),
        ("Nord", new Color("#232833"), new Color("#35506e"), new Color("#4a7a70"), new Color("#2e34408c")),
        ("Dracula", new Color("#21222c"), new Color("#4a3a7d"), new Color("#8a3f6e"), new Color("#282a368c")),
        ("Tokyo Night", new Color("#16161e"), new Color("#2f3b6b"), new Color("#5c3a7a"), new Color("#1a1b268c")),
        ("Synthwave", new Color("#0d0620"), new Color("#6b1050"), new Color("#0e6b8a"), new Color("#140a2b8c")),
        ("Ember", new Color("#140c08"), new Color("#7a2c0c"), new Color("#8a6b14"), new Color("#1c110b8c")),
        ("Abyss", new Color("#030d14"), new Color("#0b3a4d"), new Color("#1a2f6b"), new Color("#06141e8c")),
        ("Verdant", new Color("#0a120c"), new Color("#1d4a2b"), new Color("#5c7014"), new Color("#0e1a118c")),
        ("Noir", new Color("#0f0f11"), new Color("#2b2b30"), new Color("#5a5a66"), new Color("#17171a8c")),
    };

    public static readonly string[] Backgrounds = { "Flow", "Mesh", "Silk", "Horizon", "Blobs", "Waves", "Aurora", "Bokeh", "Grid", "Gradient", "Solid" };

    public static IEnumerable<string> Names => All.Select(p => p.Name);

    public static (Color Bg, Color Primary, Color Secondary, Color Panel) Find(string name)
    {
        var palette = All.FirstOrDefault(p => p.Name == name);
        if (palette.Name == null) { palette = All.First(p => p.Name == DefaultName); }
        return (palette.Bg, palette.Primary, palette.Secondary, palette.Panel);
    }

    public static string BackgroundShaderPath(string name)
    {
        if (!Backgrounds.Contains(name)) { name = Backgrounds[0]; }
        return $"res://assets/shaders/backgrounds/bg_{name.ToLowerInvariant()}.gdshader";
    }
}
