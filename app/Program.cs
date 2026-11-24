using System.Diagnostics;

namespace Ecoar;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "--test-tone")
            return TestTone(args.Length >= 2 && double.TryParse(args[1], out var s) ? s : 5);
        if (args.Length >= 1 && args[0] is "--setup" or "--uninstall")
            return Setup.Run(args);

        using var single = new Mutex(true, @"Local\Ecoar-SingleInstance", out bool first);
        if (!first) return 0;   // already running (in the tray)
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        return 0;
    }

    /// <summary>Writes an 880 Hz sine (amplitude 0.25) into the ring, paced 40 ms ahead of wall-clock.</summary>
    private static int TestTone(double seconds)
    {
        // The APO only creates the ring while some program is capturing from the mic.
        RingWriter? opened = null;
        for (int tries = 0; tries < 30 && (opened = RingWriter.TryOpen()) is null; tries++) Thread.Sleep(100);
        using var ring = opened;
        if (ring is null)
        {
            Console.Error.WriteLine("efeito inativo");
            return 2;
        }
        uint rate = ring.SampleRate;
        int chunk = (int)(rate / 100);   // 10 ms
        var buf = new float[chunk];
        long written = 0, total = (long)(seconds * rate);
        double phase = 0, inc = 2 * Math.PI * 880 / rate;
        var clock = Stopwatch.StartNew();
        while (written < total)
        {
            long due = (long)((clock.Elapsed.TotalSeconds + 0.040) * rate);
            while (written < due && written < total)
            {
                for (int i = 0; i < chunk; i++) { buf[i] = (float)(0.25 * Math.Sin(phase)); phase += inc; }
                ring.Write(buf);
                written += chunk;
            }
            Thread.Sleep(5);
        }
        return 0;
    }
}
