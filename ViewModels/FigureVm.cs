using ToyPadMaui.Models;

namespace ToyPadMaui.ViewModels;

// Lightweight view item for the library CollectionView.
public class FigureVm
{
    public string Name { get; init; } = "";
    public string RelPath { get; init; } = "";
    public string? Thumb { get; init; }
    public FigureCategory Category { get; init; }

    public static FigureVm From(Figure f) => new()
    {
        Name = f.Name,
        RelPath = f.RelPath,
        Thumb = f.ThumbFile,
        Category = f.Category,
    };
}
