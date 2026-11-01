using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;

public sealed class SmartReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DiskId { get; set; }
    public string ReportHash { get; set; } = "";
    public DateTime ImportedUtc { get; set; } = DateTime.UtcNow;
    public string Actor { get; set; } = "";
    public string CuratedJson { get; set; } = "";
}
public record SmartReportInput(string Json, string ContinuityGroup = "", bool ConfirmAtaCounterUnits = false, string? ExpectedHash = null, string? ConfirmedSerial = null);
public record AtaReportAttribute(int Id, string? Name, string? RawNumeric, string? RawText, int? NormalizedValue, int? Worst, int? Threshold, string? WhenFailed);
public record ReportReading(string Metric, double? Value, string Unit, string Quality, string? MissingReason);
public record ReportDeviceInfo(string? Firmware, string FirmwareField, string? Interface, string InterfaceField, string Quality);
public record OverallSmartResult(bool? Passed, string Quality);
public record SmartReportPreview(string ReportHash, string Protocol, string ToolVersion, string Serial, string Model, DateTime? ObservedUtc,
    int ExitStatus, bool AtaCounterUnitsConfirmed, string ContinuityGroup, string SourceSchema, ReportReading[] Readings, AtaReportAttribute[] Attributes, string[] Warnings, string? LegacyReportHash = null, string? PreviousReportHash = null, OverallSmartResult? OverallSmart = null, string? BeforeOverallReportHash = null, string? ScsiV1ReportHash = null, ReportDeviceInfo? DeviceInfo = null, string? BeforeDeviceInfoHash = null, AtaSelfTestLog[]? AtaSelfTests = null, string? BeforeSelfTestsHash = null);

public static class SmartReports
{
    public const int MaximumLength = 1024 * 1024;
    public const string Source = "Imported smartctl JSON report";
    private static readonly (int Id, string Name, string Metric)[] AtaCounters = [
        (5, "Reallocated_Sector_Ct", "ata.reallocated_sectors"), (197, "Current_Pending_Sector", "ata.pending_sectors"),
        (198, "Offline_Uncorrectable", "ata.offline_uncorrectable"), (199, "UDMA_CRC_Error_Count", "ata.crc_errors")
    ];
    public static SmartReportPreview Parse(Disk disk, SmartReportInput input, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(input.Json) || Encoding.UTF8.GetByteCount(input.Json) > MaximumLength) throw new InvalidDataException("Provide a JSON report no larger than 1 MiB.");
        if (input.ContinuityGroup is null || input.ContinuityGroup.Length > 200) throw new InvalidDataException("Continuity group must be at most 200 characters; leave it blank when unknown.");
        using var document = JsonDocument.Parse(input.Json, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || HasDuplicateProperties(root)) throw new InvalidDataException("Expected one report object with unique property names.");
        var format = Get(root, "json_format_version");
        if (format.ValueKind != JsonValueKind.Array || format.GetArrayLength() != 2 || Int(format[0]) != 1 || Int(format[1]) != 0)
            throw new InvalidDataException("Only smartctl JSON format 1.0 is supported.");
        var version = Get(Get(root, "smartctl"), "version");
        if (version.ValueKind != JsonValueKind.Array || version.GetArrayLength() != 2 || Int(version[0]) != 7 || Int(version[1]) is not {} minor || minor is < 0 or > 99)
            throw new InvalidDataException("Expected a smartctl 7.x report with a recorded tool version.");
        var toolVersion = $"7.{minor}";
        var protocol = Text(Get(root, "device"), "protocol", 20);
        if (protocol is not ("ATA" or "NVMe" or "SCSI")) throw new InvalidDataException("This importer supports ATA, NVMe and SCSI reports; other protocols are not supported.");
        var serial = Text(root, "serial_number", 200)?.Trim(); var model = Text(root, protocol == "SCSI" ? "scsi_model_name" : "model_name", 200)?.Trim();
        if (disk.MappingConfidence == "ambiguous" || string.IsNullOrWhiteSpace(disk.Serial) || string.IsNullOrWhiteSpace(disk.Model) ||
            string.IsNullOrWhiteSpace(serial) || string.IsNullOrWhiteSpace(model) || serial != disk.Serial.Trim() || model != disk.Model.Trim())
            throw new InvalidDataException("The report serial and model must exactly match an unambiguous selected drive. Device paths are not identity evidence.");
        var exit = Int(Get(Get(root, "smartctl"), "exit_status"));
        if (exit is null or < 0 or > 255) throw new InvalidDataException("The report must include a valid smartctl exit status.");
        DateTime? observed = null;
        var epoch = Get(Get(root, "local_time"), "time_t");
        if (epoch.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)) {
            if (epoch.ValueKind != JsonValueKind.Number || !epoch.TryGetInt64(out var seconds) || seconds < 0 || seconds > new DateTimeOffset(now).ToUnixTimeSeconds())
                throw new InvalidDataException("Report time is invalid or in the future. Verify the report and source clock.");
            observed = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        }
        var warnings = new List<string> { "Imported evidence is not a complete health assessment. A matching serial/model is a report claim, not independent identity verification." };
        if (observed is null) warnings.Add("Report time is missing. Numeric readings remain unverified and cannot feed health rules.");
        var commandErrors = (exit.Value & 7) != 0;
        if (commandErrors) warnings.Add("smartctl reported command/device errors. All numeric readings remain unverified.");
        if (input.ContinuityGroup.Length == 0) warnings.Add("Counter continuity is unknown. Counter increases and automatic resolution will not be inferred.");
        var readings = new List<ReportReading>(); var attributes = new List<AtaReportAttribute>();
        void Add(string metric, double? value, string? missing, bool rawAta = false) {
            var quality = value is null ? "unknown" : observed is null || commandErrors || rawAta && !input.ConfirmAtaCounterUnits ? "unverified" : "observed";
            readings.Add(new(metric, value, ObservationValidation.CanonicalUnit(metric)!, quality, value is null ? missing ?? "Reading absent or outside the supported exact numeric range." : null));
        }
        if (protocol == "ATA") {
            if (!input.ConfirmAtaCounterUnits) warnings.Add("ATA raw counters require checking this drive model's field meanings and units before they can feed health rules. Normalized values and thresholds are not raw counts.");
            var table = Get(Get(root, "ata_smart_attributes"), "table");
            if (table.ValueKind is not (JsonValueKind.Array or JsonValueKind.Undefined or JsonValueKind.Null)) throw new InvalidDataException("Unexpected ATA attribute table.");
            var byId = new Dictionary<int, JsonElement>();
            if (table.ValueKind == JsonValueKind.Array) {
                if (table.GetArrayLength() > 256) throw new InvalidDataException("ATA attribute table exceeds supported bounds.");
                foreach (var row in table.EnumerateArray()) {
                    var id = Int(Get(row, "id"));
                    if (row.ValueKind != JsonValueKind.Object || id is null or < 1 or > 255 || !byId.TryAdd(id.Value, row)) throw new InvalidDataException("ATA attribute IDs must be unique and valid.");
                }
            }
            foreach (var definition in AtaCounters) {
                byId.TryGetValue(definition.Id, out var row);
                var name = Text(row, "name", 100); var raw = Get(row, "raw"); var rawValue = Get(raw, "value");
                attributes.Add(new(definition.Id, name, rawValue.ValueKind == JsonValueKind.Number && rawValue.GetRawText().Length <= 128 ? rawValue.GetRawText() : null,
                    Text(raw, "string", 256), Int(Get(row, "value")), Int(Get(row, "worst")), Int(Get(row, "thresh")), Text(row, "when_failed", 100)));
                Add(definition.Metric, name == definition.Name ? Count(rawValue) : null,
                    name != definition.Name ? "Expected attribute ID/name pair is absent or differs; raw semantics were not inferred." : "Raw count is missing, invalid, or too large to represent exactly.", true);
            }
        } else if (protocol == "NVMe") {
            var log = Get(root, "nvme_smart_health_information_log");
            Add("nvme.media_errors", Count(Get(log, "media_errors")), "NVMe media/data-integrity error count is missing, invalid, or too large to represent exactly.");
            foreach (var field in new[] { "critical_warning", "available_spare", "available_spare_threshold", "percentage_used" }) {
                var maximum = field is "available_spare" or "available_spare_threshold" ? 100 : 255;
                var value = Int(Get(log, field));
                Add("nvme." + field, value is >= 0 && value <= maximum ? value : null, $"NVMe {field} is absent or not a whole number from 0 to {maximum}.");
            }
            warnings.Add("NVMe percentage used is the manufacturer's endurance estimate, not a failure probability or deadline. It may exceed 100%; 255 represents values above 254%. Available spare describes device spare capacity, not a replacement drive on your shelf.");
        }
        if (protocol == "SCSI") {
            Add("scsi.grown_defects", Count(Get(root, "scsi_grown_defect_list")),
                "SCSI grown-defect element count is absent, invalid, or outside the exact numeric range.");
            foreach (var operation in new[] { "read", "write", "verify" })
                Add("scsi." + operation + "_uncorrected_errors",
                    Count(Get(Get(Get(root, "scsi_error_counter_log"), operation), "total_uncorrected_errors")),
                    $"SCSI {operation} uncorrected error count is absent, invalid, or outside the exact numeric range.");
            warnings.Add("SCSI uncorrected read, write and verify errors are separate device-reported counters. Corrected errors and correction invocations are not substituted, and these counts do not identify an affected file or failed component.");
            warnings.Add("SCSI grown defects count defect-list elements, not ATA reallocated sectors. A stable nonzero count alone does not establish a new failure; compare only reports with verified continuity. SCSI does not by itself identify a SAS interface.");
        }
        var temperature = Get(Get(root, "temperature"), "current");
        double? celsius = temperature.ValueKind == JsonValueKind.Number && temperature.TryGetDouble(out var degrees) && double.IsFinite(degrees) && degrees >= -273.15 && degrees <= 1000 ? degrees : null;
        Add("temperature", celsius, "Curated current Celsius temperature is absent or outside the supported range.");
        Add("power_on_hours", Count(Get(Get(root, "power_on_time"), "hours")), "Curated power-on hours are missing, invalid, or too large to represent exactly.");
        Add("power_cycles", Count(Get(root, "power_cycle_count")), "Curated power-cycle count is missing, invalid, or too large to represent exactly.");
        warnings.Add("Power cycles are operating history, not a count of unsafe shutdowns, remaining life, or a reason by themselves to replace a drive.");
        var overallValue = Get(Get(root, "smart_status"), "passed");
        bool? overallPassed = overallValue.ValueKind is JsonValueKind.True or JsonValueKind.False ? overallValue.GetBoolean() : null;
        var overall = new OverallSmartResult(overallPassed, overallPassed is null ? "unknown" :
            observed is null || commandErrors || overallPassed == true && (exit.Value & 8) != 0 ? "unverified" : "observed");
        warnings.Add("The overall SMART result is a report observation, not a self-test completion record or a prediction. A passed result never overrides adverse counters, warnings, or recovery gaps.");
        if (overallPassed == false) warnings.Add("The report's overall SMART assessment failed. Review current native details and independent recovery readiness promptly; no failure date is inferred.");
        if (overall.Quality == "unverified") warnings.Add("Overall SMART status is unverified because report time, command errors, or contradictory exit status prevents reliable interpretation.");
        // Preserve only explicit source fields. Protocol alone does not establish a physical interface.
        string? DeviceText(JsonElement value, string field) {
            var text = Text(value, field, 200)?.Trim();
            return string.IsNullOrWhiteSpace(text) || text.Any(char.IsControl) ? null : text;
        }
        var firmwareField = protocol == "SCSI" ? "scsi_revision" : "firmware_version";
        var interfaceField = protocol == "ATA" ? "sata_version.string" : protocol == "NVMe" ? "nvme_version.string" : "scsi_transport_protocol.name";
        var interfaceValue = protocol == "ATA" ? DeviceText(Get(root, "sata_version"), "string") :
            protocol == "NVMe" ? DeviceText(Get(root, "nvme_version"), "string") : DeviceText(Get(root, "scsi_transport_protocol"), "name");
        var firmware = DeviceText(root, firmwareField);
        var deviceInfo = new ReportDeviceInfo(firmware, firmwareField, interfaceValue, interfaceField,
            firmware is null && interfaceValue is null ? "unknown" : observed is null || commandErrors ? "unverified" : "observed");
        var selfTests = protocol == "ATA" ? Homelab.Api.AtaSelfTests.Parse(root, observed is not null && !commandErrors) : null;
        if (protocol == "ATA") warnings.Add("Self-test entries are historical report evidence. Lifetime hours are device counters, not calendar timestamps; logs may wrap or be incomplete. A completed test does not prove current health or recoverability, and this application never starts a test.");
        var schema = $"smartctl-json-1.0-{protocol.ToLowerInvariant()}-tool-{toolVersion}-v{(protocol == "NVMe" ? 5 : protocol == "SCSI" ? 3 : 5)}";
        // Hash only curated source content. Whitespace, unknown fields and importer choices cannot
        // turn the same report into another sample or silently upgrade its original provenance.
        string Hash(IEnumerable<ReportReading> items) {
            var canonical = JsonSerializer.Serialize(new { protocol, toolVersion, serial, model, observed, exit,
                values = items.Select(x => new { x.Metric, x.Value, x.Unit, x.MissingReason }), attributes });
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        }
        string WithOverall(string beforeOverallHash) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { beforeOverallHash, overallPassed }))));
        var beforeOverallHash = Hash(readings);
        var beforeDeviceInfoHash = WithOverall(beforeOverallHash);
        var beforeSelfTestsHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            beforeDeviceInfoHash, firmware, firmwareField, interfaceValue, interfaceField }))));
        var hash = protocol == "ATA" ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { beforeSelfTestsHash, selfTests })))) : beforeSelfTestsHash;
        var scsiV1Hash = protocol == "SCSI" ? WithOverall(Hash(readings.Where(x => !x.Metric.EndsWith("_uncorrected_errors")))) : null;
        // Prior importers retained only these NVMe fields. Reject replay of their existing
        // reports rather than treating newly supported fields as a second observation.
        var previousHash = Hash(readings.Where(x => x.Metric != "power_cycles"));
        var legacyHash = protocol == "NVMe" ? Hash(readings.Where(x => x.Metric is "nvme.media_errors" or "temperature" or "power_on_hours")) : previousHash;
        return new(hash, protocol, toolVersion, serial!, model!, observed, exit.Value, input.ConfirmAtaCounterUnits, input.ContinuityGroup, schema, readings.ToArray(), attributes.ToArray(), warnings.ToArray(), legacyHash, previousHash, overall, beforeOverallHash, scsiV1Hash, deviceInfo, beforeDeviceInfoHash, selfTests, protocol == "ATA" ? beforeSelfTestsHash : null);
    }

    private static JsonElement Get(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var found) ? found : default;
    private static string? Text(JsonElement value, string name, int limit) => Get(value, name) is var found && found.ValueKind == JsonValueKind.String && found.GetString()!.Length <= limit ? found.GetString() : null;
    private static int? Int(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) ? n : null;
    private static double? Count(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) && n >= 0 && n <= 9007199254740991L ? n : null;
    private static bool HasDuplicateProperties(JsonElement element) => element.ValueKind switch {
        JsonValueKind.Object => element.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != element.EnumerateObject().Count() || element.EnumerateObject().Any(x => HasDuplicateProperties(x.Value)),
        JsonValueKind.Array => element.EnumerateArray().Any(HasDuplicateProperties), _ => false
    };
    public record HistoryItem(Guid Id, string Actor, DateTime ImportedUtc, SmartReportPreview? Report);
    public record HistoryPage(DateTime AsOf, HistoryItem[] Items, int? NextOffset);
    public static async Task<HistoryPage> ReadHistoryAsync(HomelabDb db, Guid diskId, DateTime asOf, int offset = 0, CancellationToken ct = default)
    {
        var rows = await db.SmartReports.AsNoTracking().Where(x => x.DiskId == diskId && x.ImportedUtc <= asOf)
            .OrderByDescending(x => x.ImportedUtc).ThenBy(x => x.Id).Skip(offset).Take(51).ToArrayAsync(ct);
        return new(asOf, rows.Take(50).Select(x => new HistoryItem(x.Id, x.Actor, x.ImportedUtc,
            JsonSerializer.Deserialize<SmartReportPreview>(x.CuratedJson))).ToArray(), rows.Length > 50 ? offset + 50 : null);
    }
    public static void MapSmartReports(this RouteGroupBuilder api)
    {
        api.MapPost("/disks/{id:guid}/smart-reports/preview", async (Guid id, SmartReportInput input, HomelabDb db) => {
            var disk = await db.Disks.FindAsync(id); if (disk is null) return Results.NotFound();
            try { return Results.Ok(Parse(disk, input, DateTime.UtcNow)); }
            catch (JsonException) { return Results.BadRequest(new { error = "The report is not supported JSON or exceeds nesting limits." }); }
            catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
        api.MapPost("/disks/{id:guid}/smart-reports", async (Guid id, SmartReportInput input, HomelabDb db, HttpContext context) => {
            var disk = await db.Disks.FindAsync(id); if (disk is null) return Results.NotFound();
            SmartReportPreview preview;
            try { preview = Parse(disk, input, DateTime.UtcNow); }
            catch (JsonException) { return Results.BadRequest(new { error = "The report is not supported JSON or exceeds nesting limits." }); }
            catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
            if (input.ExpectedHash != preview.ReportHash || input.ConfirmedSerial != disk.Serial.Trim()) return Results.BadRequest(new { error = "Preview this exact report and retype the selected drive serial before importing." });
            if (await db.Disks.AnyAsync(x => x.ConnectionId == disk.ConnectionId && x.Id != disk.Id && x.Serial == disk.Serial && x.Model == disk.Model)) return Results.BadRequest(new { error = "Another drive has the same model and serial. Resolve identity ambiguity first." });
            if (await db.SmartReports.AnyAsync(x => x.DiskId == id && (x.ReportHash == preview.ReportHash || x.ReportHash == preview.LegacyReportHash || x.ReportHash == preview.PreviousReportHash || x.ReportHash == preview.BeforeOverallReportHash || x.ReportHash == preview.ScsiV1ReportHash || x.ReportHash == preview.BeforeDeviceInfoHash || x.ReportHash == preview.BeforeSelfTestsHash))) return Results.Conflict(new { error = "This report was already imported. Existing evidence is immutable." });
            // Persist the overall assessment as its own binary observation, not a
            // replacement for any detailed metric. One means reported failure.
            var overall = preview.OverallSmart;
            var importedReadings = preview.Readings.Append(new ReportReading("smart.overall_failed",
                overall?.Passed is {} passed ? passed ? 0 : 1 : null, "flag",
                overall?.Quality ?? "unknown", overall?.Passed is null ? "Overall SMART result was not reported as a boolean." : null));
            var observations = importedReadings.Select(x => new Observation { SourceKey = $"smartctl:{id:N}:{preview.ReportHash}:{x.Metric}",
                Metric = x.Metric, Value = x.Value, Unit = x.Unit, Quality = x.Quality, MissingReason = x.MissingReason,
                ObservedUtc = preview.ObservedUtc, Source = Source, SourceSchema = preview.SourceSchema, ResetEpoch = preview.ContinuityGroup }).ToArray();
            foreach (var observation in observations) {
                if (ObservationValidation.Validate(observation, DateTime.UtcNow) is {} error) return Results.BadRequest(new { error });
                if (await db.Observations.AnyAsync(x => x.ConnectionId == disk.ConnectionId && x.SourceKey == observation.SourceKey)) return Results.Conflict(new { error = "Report evidence identifiers already exist; no readings were imported." });
            }
            await using var transaction = await db.Database.BeginTransactionAsync();
            var report = new SmartReport { DiskId = id, ReportHash = preview.ReportHash, Actor = context.User.Identity!.Name!, CuratedJson = JsonSerializer.Serialize(preview) };
            db.SmartReports.Add(report);
            await SelfTestFindings.StageAsync(db, disk, report, preview);
            foreach (var observation in observations) await ObservationIngestion.StageAsync(db, disk, observation, report.Actor);
            db.Activity.Add(new() { Actor = report.Actor, Action = "SMART report imported", Detail = $"Disk {id}; report {report.Id}; {observations.Length} curated readings." });
            await db.SaveChangesAsync(); await transaction.CommitAsync();
            return Results.Ok(new { report.Id, count = observations.Length });
        });
        api.MapGet("/disks/{id:guid}/smart-reports/history", async (Guid id, DateTime? asOf, int? offset, HomelabDb db, CancellationToken ct) => {
            var now = DateTime.UtcNow;
            if (offset < 0 || offset > int.MaxValue - 50 || offset > 0 && asOf is null || asOf is { Kind: not DateTimeKind.Utc } || asOf > now)
                return Results.BadRequest(new { error = "Use a valid UTC history snapshot and nonnegative offset." });
            if (!await db.Disks.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            return Results.Ok(await ReadHistoryAsync(db, id, asOf ?? now, offset ?? 0, ct));
        });
        api.MapGet("/disks/{id:guid}/smart-reports", async (Guid id, HomelabDb db) => {
            if (!await db.Disks.AnyAsync(x => x.Id == id)) return Results.NotFound();
            var rows = await db.SmartReports.AsNoTracking().Where(x => x.DiskId == id).OrderByDescending(x => x.ImportedUtc).Take(50).ToListAsync();
            return Results.Ok(rows.Select(x => new { x.Id, x.Actor, x.ImportedUtc, report = JsonSerializer.Deserialize<SmartReportPreview>(x.CuratedJson) }));
        });
    }
}
