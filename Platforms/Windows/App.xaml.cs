using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ToyPadMaui.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		this.InitializeComponent();

		// WinUI captura aquí las "stowed exceptions" (0xC000027B) que el
		// AppDomain de .NET no ve. Volcamos a un archivo en el escritorio.
		this.UnhandledException += (s, e) =>
		{
			try
			{
				var path = System.IO.Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
					"legopatcher_crash.txt");
				System.IO.File.AppendAllText(path,
					$"=== {DateTime.Now:O} [WinUI.UnhandledException] ===\n" +
					$"Message: {e.Message}\n{e.Exception}\n\n");
			}
			catch { }
		};
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

