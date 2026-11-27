// iRecord - process entry point.
// Chooses between headless CLI execution and the single-instance tray application.
using iRecord.App;
using iRecord.Cli;

namespace iRecord;

/// <summary>
/// Process entry point. Arguments are delegated to <see cref="iRecord.Cli.CliRunner"/>;
/// without arguments a named mutex keeps a single tray instance alive.
/// </summary>
internal static class Program
{
    /// <summary>Parses CLI arguments or starts one tray instance guarded by a named mutex.</summary>
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Length > 0)
            return CliRunner.Run(args);

        using var mutex = new Mutex(true, "iRecord.SingleInstance.v1", out bool createdNew);
        if (!createdNew)
            return 0;

        Application.Run(new TrayApplicationContext());
        return 0;
    }
}
