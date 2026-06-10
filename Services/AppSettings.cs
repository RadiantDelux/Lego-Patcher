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

    // UI language: "es" (Spanish) or "en" (English). Defaults to the device
    // language if it's Spanish, else English.
    public static string Language
    {
        get => Preferences.Get(nameof(Language), DefaultLanguage());
        set => Preferences.Set(nameof(Language), value);
    }

    static string DefaultLanguage()
    {
        try
        {
            var two = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return two == "es" ? "es" : "en";
        }
        catch { return "en"; }
    }

    public static bool IsEnglish => string.Equals(Language, "en", StringComparison.OrdinalIgnoreCase);

    // True once the user has explicitly picked a language on first run.
    public static bool LanguageChosen
    {
        get => Preferences.Get(nameof(LanguageChosen), false);
        set => Preferences.Set(nameof(LanguageChosen), value);
    }

    // True once the "how vehicles/gadgets are saved" tip has been shown.
    public static bool VehicleTipShown
    {
        get => Preferences.Get(nameof(VehicleTipShown), false);
        set => Preferences.Set(nameof(VehicleTipShown), value);
    }

    public static bool IsPs4 => string.Equals(Platform, "ps4", StringComparison.OrdinalIgnoreCase);

    public static bool DimensionsImported
    {
        get => Preferences.Get(nameof(DimensionsImported), false);
        set => Preferences.Set(nameof(DimensionsImported), value);
    }

    // ---- Pad light-zone geometry (editor) -----------------------------------
    // Each zone is x,y,w,h as a fraction of the pad image (0..1). Defaults are
    // measured from legoportal.png; the in-app editor can fine-tune them.
    public static double GetZone(string key, double def) => Preferences.Get("zone_" + key, def);
    public static void SetZone(string key, double v) => Preferences.Set("zone_" + key, v);

    public static void ResetZones()
    {
        var keys = new System.Collections.Generic.List<string>();
        foreach (var id in new[] { "L1","L2","L3","C","R1","R2","R3" })
        {
            keys.Add("S" + id + "x"); keys.Add("S" + id + "y");
            keys.Add("S" + id + "w"); keys.Add("S" + id + "h");
            keys.Add("R" + id + "x"); keys.Add("R" + id + "y"); keys.Add("R" + id + "z");
        }
        foreach (var k in keys) Preferences.Remove("zone_" + k);
    }
}
