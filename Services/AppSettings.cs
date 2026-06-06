namespace ToyPadMaui.Services;

// Persists PS3 connection info using MAUI Preferences (cross-platform).
public static class AppSettings
{
    public static string Host
    {
        get => Preferences.Get(nameof(Host), "");
        set => Preferences.Set(nameof(Host), value);
    }

    public static string User
    {
        get => Preferences.Get(nameof(User), "anonymous");
        set => Preferences.Set(nameof(User), value);
    }

    public static string Pass
    {
        get => Preferences.Get(nameof(Pass), "");
        set => Preferences.Set(nameof(Pass), value);
    }

    public static bool DimensionsImported
    {
        get => Preferences.Get(nameof(DimensionsImported), false);
        set => Preferences.Set(nameof(DimensionsImported), value);
    }
}
