namespace ToyPadMaui.Models;

public enum FigureCategory { Character, Vehicle, Gadget }

public class Figure
{
    public string Name { get; set; } = "";
    public string RelPath { get; set; } = "";      // e.g. Characters/Batman.bin
    public FigureCategory Category { get; set; }
    public byte[] Bytes { get; set; } = [];
    public string? ThumbFile { get; set; }          // raw asset filename or null

    public string CategoryShort => Category switch
    {
        FigureCategory.Character => "PJ",
        FigureCategory.Vehicle => "VEH",
        FigureCategory.Gadget => "GAD",
        _ => ""
    };
}
