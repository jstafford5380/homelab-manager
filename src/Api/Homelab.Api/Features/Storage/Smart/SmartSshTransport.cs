using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public sealed class SmartSshTransport : ISmartSshTransport
{
    public static bool ValidDevice(string device) => Regex.IsMatch(device, @"\A/dev/[A-Za-z0-9][A-Za-z0-9_.-]{0,127}\z", RegexOptions.CultureInvariant);
    public static bool ValidCommand(string command) => command == "smartctl --scan" || command.StartsWith("smartctl --json -x -n standby ", StringComparison.Ordinal) && ValidDevice(command["smartctl --json -x -n standby ".Length..]);
    public static bool ValidServer(SmartSshServer s) => s.ConnectionId != Guid.Empty && s.Port is >= 1 and <= 65535 && s.IntervalHours is >= 6 and <= 12 && Regex.IsMatch(s.Host, @"\A[A-Za-z0-9][A-Za-z0-9.:-]{0,252}\z") && Regex.IsMatch(s.User, @"\A[a-z_][a-z0-9_-]{0,31}\z") && Path.IsPathFullyQualified(s.KeyPath) && Path.IsPathFullyQualified(s.KnownHostsPath);
    public async Task<SmartCommandResult> RunAsync(SmartSshServer server, string command, CancellationToken ct)
    {
        if (!ValidServer(server) || !ValidCommand(command))
            throw new InvalidDataException("Invalid SMART SSH configuration or command.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(75));
        var start = new ProcessStartInfo("ssh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in new[]
        {
            "-F",
            "/dev/null",
            "-T",
            "-S",
            "none",
            "-o",
            "BatchMode=yes",
            "-o",
            "StrictHostKeyChecking=yes",
            "-o",
            "IdentitiesOnly=yes",
            "-o",
            "ForwardAgent=no",
            "-o",
            "ClearAllForwardings=yes",
            "-o",
            "ProxyCommand=none",
            "-o",
            "ProxyJump=none",
            "-o",
            "ConnectTimeout=15",
            "-o",
            "UserKnownHostsFile=" + server.KnownHostsPath,
            "-o",
            "GlobalKnownHostsFile=/dev/null",
            "-i",
            server.KeyPath,
            "-p",
            server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-l",
            server.User,
            "--",
            server.Host,
            command
        }

        )
            start.ArgumentList.Add(arg);
        using var process = new Process
        {
            StartInfo = start
        };
        process.Start();
        async Task<string> Bounded(StreamReader reader, int limit)
        {
            var result = new StringBuilder();
            var buffer = new char[4096];
            while (true)
            {
                var n = await reader.ReadAsync(buffer.AsMemory(), timeout.Token);
                if (n == 0)
                    return result.ToString();
                if (result.Length + n > limit)
                {
                    timeout.Cancel();
                    throw new InvalidDataException("SMART SSH response exceeded its limit.");
                }

                result.Append(buffer, 0, n);
            }
        }

        try
        {
            var output = Bounded(process.StandardOutput, SmartReports.MaximumLength);
            var error = Bounded(process.StandardError, 16384);
            await Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token));
            return new(process.ExitCode, await output);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }
}
