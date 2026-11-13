using System.Diagnostics;
using System.Runtime.InteropServices;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
var processOnly = args.FirstOrDefault() == "--process-only";
if (processOnly) args = args.Skip(1).ToArray();
var runId = Environment.GetEnvironmentVariable("NWFM_E2E_OWNER_RUN_ID");
if (!processOnly && (!Guid.TryParseExact(runId, "D", out _) || runId != runId.ToLowerInvariant()))
    throw new ArgumentException("A fresh owned e2e run identity is required.");
if (args.Length < 3) throw new ArgumentException("Expected Node, gated bootstrap and child command.");
var job = Native.CreateJobObject(IntPtr.Zero, null);
if (job == IntPtr.Zero) throw new InvalidOperationException("Cannot create owned process job.");
var code = 3;
try
{
    var limits = new Native.ExtendedLimits();
    limits.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
    if (!Native.SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf(limits)))
        throw new InvalidOperationException("Cannot configure owned process job.");
    var start = new ProcessStartInfo(args[0]) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    foreach (var argument in args.Skip(1)) start.ArgumentList.Add(argument);
    using var child = Process.Start(start) ?? throw new InvalidOperationException("Cannot start owned bootstrap.");
    _ = child.SafeHandle; // Retain the actual handle, never reacquire a stale numeric PID.
    if (!Native.AssignProcessToJobObject(job, child.SafeHandle.DangerousGetHandle()))
    {
        child.Kill(); // Bootstrap has not been admitted and cannot spawn descendants.
        await child.WaitForExitAsync();
        throw new InvalidOperationException("Cannot assign owned bootstrap to job.");
    }
    Console.WriteLine($"E2E_OWNER_STARTED run={runId} pid={child.Id} created={child.StartTime.ToUniversalTime():O}");
    await child.StandardInput.WriteLineAsync("owned");
    child.StandardInput.Close();
    var exited = child.WaitForExitAsync();
    if (await Task.WhenAny(exited, Task.Run(Console.ReadLine)) != exited)
        Native.TerminateJobObject(job, 1); // Stop request or wrapper EOF; keep cleanup outside the owned job.
    await exited;
    code = child.ExitCode;
}
finally
{
    // The runner may terminate its app-command Node on Windows before JS signal cleanup runs.
    // Every descendant was admitted through this job, including detached Angular/esbuild hosts.
    Native.TerminateJobObject(job, 1);
    var empty = false;
    for (var attempt = 0; attempt < 100; attempt++)
    {
        if (Native.QueryInformationJobObject(job, 1, out var accounting, (uint)Marshal.SizeOf<Native.Accounting>(), IntPtr.Zero)
            && accounting.ActiveProcesses == 0) { empty = true; break; }
        await Task.Delay(50); // OS teardown readiness, never browser-test synchronization.
    }
    Native.CloseHandle(job);
    if (!empty) { Console.Error.WriteLine("E2E_OWNER_JOB_NOT_EMPTY"); code = 3; }
    else Console.WriteLine("E2E_OWNER_JOB_EMPTY");
    if (!processOnly)
    {
        try
        {
            var ids = await Docker("ps", "-aq", "--filter", "label=nwfm.e2e.run=" + runId);
            foreach (var id in ids.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var identity = await Docker("inspect", "--format", "{{.Name}}|{{index .Config.Labels \"nwfm.e2e.run\"}}", id);
                if (identity.Trim() != "/nwfm-e2e-" + runId + "|" + runId)
                    throw new InvalidOperationException("Owned container identity mismatch; refusing removal.");
                await Docker("rm", "--force", id);
            }
            if (!string.IsNullOrWhiteSpace(await Docker("ps", "-aq", "--filter", "label=nwfm.e2e.run=" + runId)))
                throw new InvalidOperationException("Owned container remains.");
            Console.WriteLine("E2E_OWNER_CONTAINER_ABSENT run=" + runId);
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); code = 3; }
    }
}
return code;

static async Task<string> Docker(params string[] arguments)
{
    var start = new ProcessStartInfo("docker") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var command = Process.Start(start) ?? throw new InvalidOperationException("Cannot inspect owned Docker resources.");
    command.StandardInput.Close();
    var stdout = command.StandardOutput.ReadToEndAsync();
    var stderr = command.StandardError.ReadToEndAsync();
    try
    {
        await command.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(180));
    }
    catch
    {
        if (!command.HasExited) command.Kill();
        await command.WaitForExitAsync();
        throw;
    }
    if (command.ExitCode != 0) throw new InvalidOperationException("Owned Docker cleanup command failed.");
    await stderr;
    return await stdout;
}

static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct BasicLimits { public long ProcessTime, JobTime; public uint LimitFlags; public nuint Minimum, Maximum; public uint ActiveLimit; public nuint Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] internal struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] internal struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [StructLayout(LayoutKind.Sequential)] internal struct Accounting { public long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime; public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll")] internal static extern bool SetInformationJobObject(IntPtr job, int kind, ref ExtendedLimits info, uint size);
    [DllImport("kernel32.dll")] internal static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] internal static extern bool TerminateJobObject(IntPtr job, uint code);
    [DllImport("kernel32.dll")] internal static extern bool QueryInformationJobObject(IntPtr job, int kind, out Accounting info, uint size, IntPtr returned);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
}
