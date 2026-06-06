using Foundation;
using ToyPadMaui.Services;
using UIKit;

namespace ToyPadMaui;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	// Handle "Open with / Share to LEGOPATCHER" of a .zip from the Files app.
	// This is the reliable path on iOS 26 where the in-app document picker
	// Select button is broken (Apple bug). The user opens the zip from Files
	// and shares it to this app instead.
	public override bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options)
	{
		_ = HandleIncomingZipAsync(url);
		return true;
	}

	static async Task HandleIncomingZipAsync(NSUrl url)
	{
		if (url is null) return;
		try
		{
			bool security = url.StartAccessingSecurityScopedResource();
			try
			{
				var path = url.Path;
				if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
				var bytes = await File.ReadAllBytesAsync(path);
				await IncomingFile.HandleZipBytesAsync(bytes);
			}
			finally
			{
				if (security) url.StopAccessingSecurityScopedResource();
			}
		}
		catch { /* ignore */ }
	}
}
