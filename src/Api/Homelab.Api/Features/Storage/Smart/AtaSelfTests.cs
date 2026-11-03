using System.Text.Json;

namespace Homelab.Api;
public static class AtaSelfTests
{
    public static AtaSelfTestLog[] Parse(JsonElement root, bool verified)
    {
        var container = Get(root, "ata_smart_self_test_log");
        if (container.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return[];
        if (container.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Unexpected ATA self-test log object.");
        var logs = new List<AtaSelfTestLog>();
        foreach (var name in new[]
        {
            "standard",
            "extended"
        }

        )
        {
            var log = Get(container, name);
            if (log.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                continue;
            if (log.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Unexpected ATA self-test log section.");
            var count = Number(Get(log, "count"), 9007199254740991);
            var table = Get(log, "table");
            if (table.ValueKind is not (JsonValueKind.Array or JsonValueKind.Undefined or JsonValueKind.Null))
                throw new InvalidDataException("Unexpected ATA self-test entry table.");
            if (table.ValueKind == JsonValueKind.Array && table.GetArrayLength() > 256)
                throw new InvalidDataException("ATA self-test log exceeds the supported 256-entry limit per section.");
            var entries = new List<AtaSelfTestEntry>();
            if (table.ValueKind == JsonValueKind.Array)
                foreach (var row in table.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("ATA self-test entries must be objects.");
                    var type = Get(row, "type");
                    var status = Get(row, "status");
                    var code = (int? )Number(Get(status, "value"), 255);
                    var result = code is null ? "unknown" : (code.Value >> 4) switch
                    {
                        0 => "completed without error",
                        1 => "aborted by host",
                        2 => "interrupted by reset",
                        >= 3 and <= 8 => "failed",
                        15 => "in progress",
                        _ => "unknown"
                    };
                    var passed = Get(status, "passed");
                    bool? reportedPassed = passed.ValueKind is JsonValueKind.True or JsonValueKind.False ? passed.GetBoolean() : null;
                    // smartctl can set passed=true for in-progress/reserved results. Only the status code classifies completion.
                    if (result == "completed without error" && (reportedPassed == false || (code!.Value & 15) != 0))
                        result = "inconsistent";
                    entries.Add(new(entries.Count + 1, (int? )Number(Get(type, "value"), 255), Text(type, "string"), code, Text(status, "string"), result, (int? )Number(Get(status, "remaining_percent"), 100), Number(Get(row, "lifetime_hours"), 65535), Number(Get(row, "lba"), 281474976710654), reportedPassed));
                }

            if (count == 0 && entries.Count > 0)
                throw new InvalidDataException("ATA self-test count contradicts its entries.");
            var coverage = entries.Count > 0 ? "reported entries" : count == 0 ? "reported empty" : "entries unavailable";
            logs.Add(new("ata_smart_self_test_log." + name, coverage, (int? )Number(Get(log, "revision"), 65535), count, verified ? "observed" : "unverified", entries.ToArray()));
        }

        return logs.ToArray();
    }

    private static JsonElement Get(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) ? item : default;
    private static long? Number(JsonElement value, long max) => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) && n >= 0 && n <= max ? n : null;
    private static string? Text(JsonElement value, string name)
    {
        var item = Get(value, name);
        if (item.ValueKind != JsonValueKind.String)
            return null;
        var text = item.GetString()!;
        return string.IsNullOrWhiteSpace(text) || text.Length > 200 || text.Any(char.IsControl) ? null : text;
    }
}
