namespace ToyPadMaui2;

public partial class App : Application
{
	public App()
	{
		// Capturar cualquier excepción no controlada en arranque y volcarla a
		// un archivo en el escritorio para diagnosticar el crash de Windows
		// (STATUS_STOWED_EXCEPTION 0xC000027B no deja stderr).
		AppDomain.CurrentDomain.UnhandledException += (s, e) =>
			DumpCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
		TaskScheduler.UnobservedTaskException += (s, e) =>
			DumpCrash("UnobservedTaskException", e.Exception);

		try
		{
			InitializeComponent();
		}
		catch (Exception ex)
		{
			DumpCrash("App.InitializeComponent", ex);
			throw;
		}
	}

	static void DumpCrash(string where, Exception? ex)
	{
		try
		{
			var path = System.IO.Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
				"legopatcher_crash.txt");
			System.IO.File.AppendAllText(path,
				$"=== {DateTime.Now:O} [{where}] ===\n{ex}\n\n");
		}
		catch { /* nada que hacer */ }
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}
