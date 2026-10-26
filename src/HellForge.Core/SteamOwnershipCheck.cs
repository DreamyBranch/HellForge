using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace HellForge.Core;

// The Steam ownership check that setup and every session run (app/scripts/Test-SteamOwnership.ps1 on Windows, which runs
// app/steamprobe/HellForge.SteamProbe.exe). Whatever ran it, the evidence it writes decides: only a fresh report that
// the live Steam client confirms this app for its signed-in owner (or through Family Sharing) passes. A check that ran
// nothing passes nothing: Wine's powershell.exe is a stub that returns 0 without running the script, which let Linux
// sessions through with no check at all.
public static class SteamOwnershipCheck
{
    public const uint AppId = 553850;
    public const string Confirmed = "local_steam_client_subscription_confirmed";

    // Null when the evidence at path confirms ownership and was written at or after notBeforeUtc; else why it does not.
    public static string? Problem(string path, DateTime notBeforeUtc)
    {
        if (!File.Exists(path)) return "the check wrote no evidence";
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return "the evidence is not a report";
            if (!root.TryGetProperty("status", out var status) || status.GetString() != Confirmed)
                return root.TryGetProperty("failure", out var failure) && failure.ValueKind == JsonValueKind.String
                    ? failure.GetString() : "Steam did not confirm the game on this account";
            if (!root.TryGetProperty("localClientOwnershipConfirmed", out var confirmed) || confirmed.ValueKind != JsonValueKind.True)
                return "Steam did not confirm the game on this account";
            if (!root.TryGetProperty("appId", out var app) || !app.TryGetUInt32(out var appId) || appId != AppId)
                return "the evidence is for another app";
            if (!root.TryGetProperty("checkedUtc", out var checkedUtc) || !DateTime.TryParse(checkedUtc.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when) || (notBeforeUtc - when).TotalSeconds > 5)
                return "the evidence is not from this check";
            return null;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            return "the evidence could not be read";
        }
    }

    // The check without PowerShell (under Wine, where powershell.exe runs nothing): the probe on a fresh copy of the
    // given steam_api64.dll. Windows checks the DLL's Valve signature in the script; Wine cannot, so the DLL must be the
    // supported build's own (its depot SHA-1 from supported-build.json). Returns the probe's exit code (0 confirmed).
    public static int RunProbe(string probe, string steamApi, string workRoot, string evidencePath, TimeSpan timeout)
    {
        var expected = SupportedBuild.Current.Files.Single(f => f.Path == "bin/steam_api64.dll");
        if (!File.Exists(steamApi) || new FileInfo(steamApi).Length != expected.Size ||
            !SupportedBuild.Sha1(steamApi).Equals(expected.Sha1, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The ownership check needs the supported build's own steam_api64.dll: " + steamApi);
        if (!File.Exists(probe)) throw new InvalidDataException("The Steam ownership probe is missing (" + probe + "): reinstall HellForge.");
        var run = Directory.CreateDirectory(Path.Combine(workRoot, Guid.NewGuid().ToString("N"))).FullName;
        var library = Path.Combine(run, "steam_api64.dll");
        File.Copy(steamApi, library);
        File.WriteAllText(Path.Combine(run, "steam_appid.txt"), AppId.ToString(CultureInfo.InvariantCulture) + "\n");
        string hash;
        using (var stream = File.OpenRead(library)) hash = Convert.ToHexString(SHA256.HashData(stream));
        var report = Path.Combine(run, "result.json");
        var start = new ProcessStartInfo(probe) { WorkingDirectory = run, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { library, hash, report }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidDataException("The Steam ownership probe did not start.");
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
            throw new InvalidDataException("The Steam ownership check timed out.");
        }
        if (File.Exists(report)) File.Copy(report, evidencePath, overwrite: true);
        return process.ExitCode;
    }

    // Linux setup runs the same probe through the installation's wrapper and Proton. The wrapper keeps Steam bridged
    // and performs its namespace self-test. Some Proton versions keep the probe process alive after it has written its
    // final report, so the report is authoritative and avoids waiting for unrelated shutdown work.
    public static int RunProbeThroughLinuxWrapper(string wrapper, string proton, string installRoot, string prefix, string steamRoot,
        string probe, string steamApi, string workRoot, string evidencePath, TimeSpan timeout)
    {
        if (!File.Exists(proton)) throw new InvalidDataException("Proton was not found in Steam; install a Proton version or set PROTON.");
        var expected = SupportedBuild.Current.Files.Single(f => f.Path == "bin/steam_api64.dll");
        if (!File.Exists(steamApi) || new FileInfo(steamApi).Length != expected.Size ||
            !SupportedBuild.Sha1(steamApi).Equals(expected.Sha1, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The ownership check needs the supported build's own steam_api64.dll: " + steamApi);
        if (!File.Exists(probe)) throw new InvalidDataException("The Steam ownership probe is missing (" + probe + "): reinstall HellForge.");

        var run = Directory.CreateDirectory(Path.Combine(workRoot, Guid.NewGuid().ToString("N"))).FullName;
        var library = Path.Combine(run, "steam_api64.dll");
        File.Copy(steamApi, library);
        File.WriteAllText(Path.Combine(run, "steam_appid.txt"), AppId.ToString(CultureInfo.InvariantCulture) + "\n");
        string hash;
        using (var stream = File.OpenRead(library)) hash = Convert.ToHexString(SHA256.HashData(stream));
        var report = Path.Combine(run, "result.json");
        var start = new ProcessStartInfo("bash") { WorkingDirectory = installRoot, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(wrapper);
        start.ArgumentList.Add(WinePath.ToWinePath(probe, prefix));
        start.ArgumentList.Add(WinePath.ToWinePath(library, prefix));
        start.ArgumentList.Add(hash);
        start.ArgumentList.Add(WinePath.ToWinePath(report, prefix));
        start.Environment["INSTALL"] = installRoot;
        start.Environment["PROTON"] = proton;
        start.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = steamRoot;
        start.Environment["STEAM_COMPAT_DATA_PATH"] = prefix;
        start.Environment["HELLFORGE_WORKING_DIRECTORY"] = run;
        start.Environment["HELLFORGE_SKIP_LIVE_WAR"] = "1";
        start.Environment["HELLFORGE_WAIT_FOR_PROCESSES"] = "0";
        using var process = Process.Start(start) ?? throw new InvalidDataException("The Linux Steam ownership check did not start.");
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            if (process.WaitForExit(200))
            {
                if (File.Exists(report)) File.Copy(report, evidencePath, overwrite: true);
                return File.Exists(report) ? RunProbeReportExit(report) : process.ExitCode;
            }

            if (!File.Exists(report)) continue;
            try
            {
                var reportExit = RunProbeReportExit(report);
                File.Copy(report, evidencePath, overwrite: true);
                if (!process.WaitForExit(1000))
                {
                    try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                }
                return reportExit;
            }
            catch (JsonException)
            {
                // The probe may still be writing its result; retry until the report is complete or the deadline passes.
            }
        }

        try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        throw new InvalidDataException("The Steam ownership check timed out before writing a complete result.");
    }

    static int RunProbeReportExit(string report)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(report));
        return document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("localClientOwnershipConfirmed", out var confirmed) &&
            confirmed.ValueKind == JsonValueKind.True ? 0 : 1;
    }
}
