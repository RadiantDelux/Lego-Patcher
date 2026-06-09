namespace ToyPadMaui.Services;

// Persists console connection info using MAUI Preferences (cross-platform).
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

    // Target console: "ps3" (webMAN/multiMAN) or "ps4" (GoldHEN).
    public static string Platform
    {
        get => Preferences.Get(nameof(Platform), "ps3");
        set => Preferences.Set(nameof(Platform), value);
    }

    // Optional FTP port override. 0 = use the platform default (PS3=21, PS4=2121).
    public static int FtpPort
    {
        get => Preferences.Get(nameof(FtpPort), 0);
        set => Preferences.Set(nameof(FtpPort), value);
    }

    public static bool IsPs4 => string.Equals(Platform, "ps4", StringComparison.OrdinalIgnoreCase);

    public static bool DimensionsImported
    {
        get => Preferences.Get(nameof(DimensionsImported), false);
        set => Preferences.Set(nameof(DimensionsImported), value);
    }
}
