using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32;

namespace Ecoar;

public enum SetupAction
{
    WriteSfx, WriteModes, SkipModesAlreadyOurs, RefuseForeignSfx, RemoveSfx, RemoveModes,
    WriteMfx, WriteMfxModes, RefuseForeignMfx, RemoveMfx, RemoveMfxModes,
}

/// <summary>
/// Installs/uninstalls EcoarApo.dll on one microphone (needs admin): as the stream effect (SFX) for
/// DEFAULT-mode captures and as the mode effect (MFX) for RAW captures. Each stream passes it once.
/// </summary>
public static class Setup
{
    public const string ApoClsid = "{5E0C2A91-3B7D-4C6E-9F18-A2D4B6C8E0F1}";
    public const string DefaultMode = "{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}";
    public const string RawMode = "{9E90EA20-B493-4FD1-A1A8-7E1361A956CF}";
    public static readonly string[] OurModes = { DefaultMode };
    /// <summary>Discord captures in RAW mode, which skips every SFX; an MFX that declares RAW still runs (verified on Fuxi-H3).</summary>
    public static readonly string[] OurMfxModes = { RawMode };
    /// <summary>SFX modes lists written by earlier Ecoar builds (upgraded on install, removed on uninstall).</summary>
    private static readonly string[][] OldSfxModes = { new[] { DefaultMode, RawMode } };
    public const string SfxValueName = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},5";
    public const string ModesValueName = "{d3993a3f-99c2-4402-b5ec-a92a0367664b},5";
    public const string MfxValueName = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},6";
    public const string MfxModesValueName = "{d3993a3f-99c2-4402-b5ec-a92a0367664b},6";
    private const string NameProp = "{a45c254e-df1c-4efd-8020-67d146a850e0},2";
    private const string CaptureKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture";
    private const string InstallDir = @"C:\Program Files\Ecoar";
    private const string DataDir = @"C:\ProgramData\Ecoar";
    private const string DllName = "EcoarApo.dll";

    private static bool IsOurs(object? v) => v is string s && s.Equals(ApoClsid, StringComparison.OrdinalIgnoreCase);

    private static bool SameModes(object? v, string[] list) =>
        v is string[] a && a.Length == list.Length && a.Zip(list).All(p => p.First.Equals(p.Second, StringComparison.OrdinalIgnoreCase));

    private static bool IsOurSfxModes(object? v) => SameModes(v, OurModes) || OldSfxModes.Any(old => SameModes(v, old));

    private static bool IsOurMfxModes(object? v) => SameModes(v, OurMfxModes);

    private static object? Get(IReadOnlyDictionary<string, object?> fx, string name) => fx.TryGetValue(name, out var v) ? v : null;

    public static IReadOnlyList<SetupAction> PlanInstall(IReadOnlyDictionary<string, object?> fx)
    {
        object? sfx = Get(fx, SfxValueName), mfx = Get(fx, MfxValueName);
        if (sfx is not null && !IsOurs(sfx)) return new[] { SetupAction.RefuseForeignSfx };
        if (mfx is not null && !IsOurs(mfx)) return new[] { SetupAction.RefuseForeignMfx };
        var plan = new List<SetupAction>();
        PlanSlot(plan, sfx, Get(fx, ModesValueName), IsOurSfxModes, OurModes, SetupAction.WriteSfx, SetupAction.WriteModes);
        PlanSlot(plan, mfx, Get(fx, MfxModesValueName), IsOurMfxModes, OurMfxModes, SetupAction.WriteMfx, SetupAction.WriteMfxModes);
        return plan;
    }

    private static void PlanSlot(List<SetupAction> plan, object? clsid, object? modes, Func<object?, bool> isOurs,
                                 string[] current, SetupAction writeClsid, SetupAction writeModes)
    {
        if (clsid is null) plan.Add(writeClsid);
        // Write when absent or when it is an older list of ours; a foreign list is left alone.
        bool write = modes is null || (isOurs(modes) && !SameModes(modes, current));
        plan.Add(write ? writeModes : SetupAction.SkipModesAlreadyOurs);
    }

    public static IReadOnlyList<SetupAction> PlanUninstall(IReadOnlyDictionary<string, object?> fx)
    {
        var plan = new List<SetupAction>();
        if (IsOurs(Get(fx, SfxValueName)))
        {
            plan.Add(SetupAction.RemoveSfx);
            if (IsOurSfxModes(Get(fx, ModesValueName))) plan.Add(SetupAction.RemoveModes);
        }
        if (IsOurs(Get(fx, MfxValueName)))
        {
            plan.Add(SetupAction.RemoveMfx);
            if (IsOurMfxModes(Get(fx, MfxModesValueName))) plan.Add(SetupAction.RemoveMfxModes);
        }
        return plan;
    }

    public static IReadOnlyList<(string Id, string Name)> ActiveCaptureEndpoints()
    {
        var list = new List<(string, string)>();
        using var root = OpenHklm().OpenSubKey(CaptureKey);
        if (root is null) return list;
        foreach (string id in root.GetSubKeyNames())
        {
            using var ep = root.OpenSubKey(id);
            if (ep?.GetValue("DeviceState") is not int state || state != 1) continue;
            using var props = ep.OpenSubKey("Properties");
            list.Add((id, props?.GetValue(NameProp) as string ?? id));
        }
        return list;
    }

    /// <summary>True when the endpoint's FxProperties already points at our APO.</summary>
    public static bool IsInstalledOn(string endpointId)
    {
        using var fx = OpenHklm().OpenSubKey($@"{CaptureKey}\{endpointId}\FxProperties");
        return IsOurs(fx?.GetValue(SfxValueName));
    }

    /// <summary>Handles --setup &lt;endpointId&gt; and --uninstall. Returns a process exit code.</summary>
    public static int Run(string[] args)
    {
        if (!IsAdmin()) return RelaunchElevated(args);
        try
        {
            if (args[0] == "--setup" && args.Length >= 2) return Install(args[1]);
            if (args[0] == "--uninstall") return Uninstall();
            return 64;
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "Ecoar", MessageBoxButtons.OK, MessageBoxIcon.Error);
            TryStartAudio();
            return 1;
        }
    }

    private static int Install(string endpointId)
    {
        string fxPath = $@"{CaptureKey}\{endpointId}\FxProperties";
        using var fx = OpenHklm().OpenSubKey(fxPath, writable: true) ?? OpenHklm().CreateSubKey(fxPath, writable: true);
        var plan = PlanInstall(ReadValues(fx));
        if (plan.Contains(SetupAction.RefuseForeignSfx) || plan.Contains(SetupAction.RefuseForeignMfx))
        {
            MessageBox.Show("Esse microfone já tem um efeito de áudio de outro programa. O Ecoar não vai sobrescrever.",
                "Ecoar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 3;
        }

        string backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ecoar");
        Directory.CreateDirectory(backupDir);
        string backup = Path.Combine(backupDir, $"backup-{endpointId}-{DateTime.Now:yyyyMMdd-HHmmss}.reg");
        RunTool("reg.exe", $"export \"HKLM\\{CaptureKey}\\{endpointId}\" \"{backup}\" /y");

        Directory.CreateDirectory(DataDir);
        foreach (string sid in new[] { "*S-1-5-19", "*S-1-5-12", "*S-1-15-2-1", "*S-1-5-4" })
            RunTool("icacls.exe", $"\"{DataDir}\" /grant \"{sid}:(OI)(CI)M\"");

        StopAudio();   // audiodg may hold the old DLL
        Directory.CreateDirectory(InstallDir);
        File.Copy(Path.Combine(AppContext.BaseDirectory, DllName), Path.Combine(InstallDir, DllName), overwrite: true);
        RunTool("regsvr32.exe", $"/s \"{Path.Combine(InstallDir, DllName)}\"");

        foreach (var action in plan)
        {
            if (action == SetupAction.WriteSfx) fx.SetValue(SfxValueName, ApoClsid, RegistryValueKind.String);
            if (action == SetupAction.WriteModes) fx.SetValue(ModesValueName, OurModes, RegistryValueKind.MultiString);
            if (action == SetupAction.WriteMfx) fx.SetValue(MfxValueName, ApoClsid, RegistryValueKind.String);
            if (action == SetupAction.WriteMfxModes) fx.SetValue(MfxModesValueName, OurMfxModes, RegistryValueKind.MultiString);
        }
        StartAudio();
        return 0;
    }

    private static int Uninstall()
    {
        using (var root = OpenHklm().OpenSubKey(CaptureKey))
        {
            foreach (string id in root?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                // Many endpoints' FxProperties deny write even to admins: read first, open writable only where it is ours.
                string path = $@"{CaptureKey}\{id}\FxProperties";
                IReadOnlyList<SetupAction> plan;
                using (var ro = OpenHklm().OpenSubKey(path))
                {
                    if (ro is null) continue;
                    plan = PlanUninstall(ReadValues(ro));
                }
                if (plan.Count == 0) continue;
                using var fx = OpenHklm().OpenSubKey(path, writable: true)!;
                foreach (var action in plan)
                {
                    if (action == SetupAction.RemoveSfx) fx.DeleteValue(SfxValueName, false);
                    if (action == SetupAction.RemoveModes) fx.DeleteValue(ModesValueName, false);
                    if (action == SetupAction.RemoveMfx) fx.DeleteValue(MfxValueName, false);
                    if (action == SetupAction.RemoveMfxModes) fx.DeleteValue(MfxModesValueName, false);
                }
            }
        }
        StopAudio();
        string dll = Path.Combine(InstallDir, DllName);
        if (File.Exists(dll)) RunTool("regsvr32.exe", $"/u /s \"{dll}\"");
        if (Directory.Exists(InstallDir)) Directory.Delete(InstallDir, true);
        StartAudio();
        if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true);
        return 0;
    }

    private static Dictionary<string, object?> ReadValues(RegistryKey key)
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in key.GetValueNames()) d[name] = key.GetValue(name);
        return d;
    }

    private static RegistryKey OpenHklm() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

    private static bool IsAdmin() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    private static int RelaunchElevated(string[] args)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, string.Join(' ', args.Select(a => $"\"{a}\"")))
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            p!.WaitForExit();
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return 5;   // user declined UAC
        }
    }

    private static void RunTool(string exe, string arguments)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, arguments) { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"{exe} {arguments} falhou (código {p.ExitCode}).");
    }

    private static void StopAudio()
    {
        using var sc = new ServiceController("audiosrv");
        if (sc.Status != ServiceControllerStatus.Stopped)
        {
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
        }
    }

    private static void StartAudio()
    {
        using var sc = new ServiceController("audiosrv");
        if (sc.Status != ServiceControllerStatus.Running)
        {
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
        }
    }

    private static void TryStartAudio()
    {
        try { StartAudio(); } catch { /* best effort after a failed setup */ }
    }
}
