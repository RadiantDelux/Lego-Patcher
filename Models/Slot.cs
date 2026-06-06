namespace ToyPadMaui.Models;

// Slot ids must match the toypad_emu plugin file mapping:
//   C            = figure1.bin            (centro)
//   L1 / L2 / L3 = figure2/3/4.bin        (izquierda)
//   R1 / R2 / R3 = figure5/6/7.bin        (derecha)
public static class SlotMap
{
    public static readonly Dictionary<string, string> Files = new()
    {
        ["C"]  = "figure1.bin",
        ["L1"] = "figure2.bin", ["L2"] = "figure3.bin", ["L3"] = "figure4.bin",
        ["R1"] = "figure5.bin", ["R2"] = "figure6.bin", ["R3"] = "figure7.bin",
    };

    public static IEnumerable<string> AllSlots => Files.Keys;
}
