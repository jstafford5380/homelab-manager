using System.Globalization;
using System.Security.Cryptography;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Homelab.Api;

public record UpsDevice(string? Key, string Name, string? Model, bool HasSerial, double? NominalVa,
    UpsReading? Load, UpsReading? Charge, UpsReading? Runtime);

internal static partial class UpsNominalRating
{
    [GeneratedRegex("\\AUPS ([A-Za-z0-9_.-]{1,64}) ", RegexOptions.CultureInvariant)]
    private static partial Regex DeviceLine();

    public static async Task<UpsDevice[]?> ReadAsync(NasConnection connection, CancellationToken ct)
    {
        // A WebSocket tunnel does not establish a route to the same system on the NUT port.
        if (connection.ConnectHost is not null || !Uri.TryCreate(connection.ApiUri, UriKind.Absolute, out var uri))
            return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var token = timeout.Token;
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(uri.IdnHost, 3493, token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await stream.WriteAsync("LIST UPS\n"u8.ToArray(), token);
            if (await ReadLine(reader, token) != "BEGIN LIST UPS")
                return null;
            var names = new List<string>();
            for (var line = await ReadLine(reader, token); line != "END LIST UPS"; line = await ReadLine(reader, token))
            {
                var match = DeviceLine().Match(line ?? "");
                if (!match.Success || names.Count == 8 || names.Contains(match.Groups[1].Value))
                    return null;
                names.Add(match.Groups[1].Value);
            }

            var devices = new List<UpsDevice>();
            foreach (var name in names)
            {
                var serial = await ReadVariable("device.serial", name, stream, reader, token)
                    ?? await ReadVariable("ups.serial", name, stream, reader, token);
                var manufacturer = await ReadVariable("device.mfr", name, stream, reader, token);
                var model = await ReadVariable("device.model", name, stream, reader, token);
                var nominal = Positive(await ReadVariable("ups.power.nominal", name, stream, reader, token));
                var load = Nonnegative(await ReadVariable("ups.load", name, stream, reader, token));
                var charge = Nonnegative(await ReadVariable("battery.charge", name, stream, reader, token));
                var runtime = Nonnegative(await ReadVariable("battery.runtime", name, stream, reader, token));
                var observed = DateTime.UtcNow;
                string? key = null;
                if (serial is not null || manufacturer is not null && model is not null)
                {
                    var identity = serial is not null
                        ? new[] { "serial", manufacturer, model, serial }
                        : new[] { "connection", connection.ConfigurationRevision.ToString(), name, manufacturer, model, nominal?.ToString(CultureInfo.InvariantCulture) };
                    key = Convert.ToHexString(SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(identity)));
                }
                devices.Add(new(key, name, model, serial is not null, nominal,
                    load is { } l ? new(l, observed) : null,
                    charge is >= 0 and <= 100 ? new(charge.Value, observed) : null,
                    runtime is { } r ? new(r, observed) : null));
            }
            // Duplicate serial identities cannot safely share an estimate profile.
            return devices.Select(device => device.Key is not null && devices.Count(other => other.Key == device.Key) > 1
                ? device with { Key = null } : device).ToArray();
        }
        catch (Exception error) when (error is SocketException or IOException || error is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<string?> ReadVariable(string variable, string name, NetworkStream stream, StreamReader reader, CancellationToken ct)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET VAR {name} {variable}\n"), ct);
        var prefix = $"VAR {name} {variable} \"";
        var reply = await ReadLine(reader, ct);
        if (reply is null || reply.Length <= prefix.Length + 1 || !reply.StartsWith(prefix, StringComparison.Ordinal) || !reply.EndsWith('"'))
            return null;
        var value = reply[prefix.Length..^1].Trim();
        return value.Length is > 0 and <= 128 && !value.Any(char.IsControl) ? value : null;
    }

    private static double? Nonnegative(string? value) =>
        double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) &&
        double.IsFinite(number) && number >= 0 ? number : null;

    private static double? Positive(string? value) => Nonnegative(value) is > 0 and var number ? number : null;

    private static async Task<string?> ReadLine(StreamReader reader, CancellationToken ct)
    {
        var buffer = new char[1];
        var line = new StringBuilder();
        while (line.Length < 512 && await reader.ReadAsync(buffer.AsMemory(), ct) == 1)
        {
            if (buffer[0] == '\n')
                return line.ToString().TrimEnd('\r');
            line.Append(buffer[0]);
        }
        return null;
    }
}
