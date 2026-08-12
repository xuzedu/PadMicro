using System.IO.Pipes;

namespace PadMicro.UI;

internal static class Program
{
    internal const string ActivationPipeName = "PadMicro.UI.Activate.v1";
    private const string SingleInstanceMutexName = @"Local\PadMicro.UI.SingleInstance.v1";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var exportIndex = Array.FindIndex(args, arg => arg.Equals("--export", StringComparison.OrdinalIgnoreCase));
        if (exportIndex >= 0 && exportIndex + 1 < args.Length)
        {
            using var form = new MainForm(autoStart: false, startHidden: false);
            form.CreateControl();
            form.PerformLayout();
            form.ExportImage(Path.GetFullPath(args[exportIndex + 1]));
            return;
        }

        using var singleInstance = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            RequestExistingInstanceActivation();
            return;
        }

        var startHidden = args.Any(arg => arg.Equals("--background", StringComparison.OrdinalIgnoreCase));
        Application.Run(new MainForm(autoStart: true, startHidden));
    }

    private static void RequestExistingInstanceActivation()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", ActivationPipeName, PipeDirection.Out);
                client.Connect(350);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine("SHOW");
                return;
            }
            catch (TimeoutException) { }
            catch (IOException) { }
            Thread.Sleep(120);
        }
    }
}
