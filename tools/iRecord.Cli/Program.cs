using iRecord.Cli;

namespace iRecord.CliLauncher;

internal static class Program
{
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
