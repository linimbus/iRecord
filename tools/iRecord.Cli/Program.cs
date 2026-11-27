// iRecord - dedicated console launcher.
// Initializes DPI/WinForms for headless recording borders, then delegates to CliRunner.
using iRecord.Cli;

namespace iRecord.CliLauncher;

/// <summary>
/// Console entry point for iRecord-cli. Keeps DPI/WinForms initialization out of the tray app.
/// </summary>
internal static class Program
{
    /// <summary>Initializes WinForms/DPI, then runs CliRunner and maps failures to exit code 1.</summary>
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
        }
        catch
        {
        }

        if (args.Length == 0)
        {
            Console.Error.WriteLine("iRecord headless CLI. Run with \"help\" for usage.");
            return 2;
        }

        try
        {
            return CliRunner.Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            return 1;
        }
    }
}
