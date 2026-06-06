using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ToyPadMaui.ViewModels;

public class SlotVm : INotifyPropertyChanged
{
    public string Id { get; init; } = "";   // C, L1.. R3
    public string Tag { get; init; } = "";   // shown in corner

    string _name = "";
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(Filled)); OnPropertyChanged(nameof(DisplayName)); } }

    string? _thumb;
    public string? Thumb { get => _thumb; set { _thumb = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasThumb)); } }

    string _relPath = "";
    public string RelPath { get => _relPath; set { _relPath = value; OnPropertyChanged(); } }

    public bool Filled => !string.IsNullOrEmpty(_relPath);
    public bool HasThumb => Filled && !string.IsNullOrEmpty(_thumb);
    public string DisplayName => Filled ? _name : "vacío";

    public void Set(string name, string relPath, string? thumb)
    {
        _name = name; _relPath = relPath; _thumb = thumb;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(RelPath));
        OnPropertyChanged(nameof(Thumb));
        OnPropertyChanged(nameof(Filled));
        OnPropertyChanged(nameof(HasThumb));
        OnPropertyChanged(nameof(DisplayName));
    }

    public void Clear() => Set("", "", null);

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
