using iRecord.App;
using iRecord.Cli;

namespace iRecord;

internal static class Program
{
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
