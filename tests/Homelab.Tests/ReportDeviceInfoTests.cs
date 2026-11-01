using System.Text.Json;
using System.Text.Json.Nodes;
using Homelab.Api;
namespace Homelab.Tests;
public sealed class ReportDeviceInfoTests
{
    private static readonly DateTime Now = new(2026,11,1,12,0,0,DateTimeKind.Utc);
    private static JsonObject Report(string protocol) => new() {
        ["json_format_version"] = new JsonArray(1,0), ["smartctl"] = new JsonObject { ["version"] = new JsonArray(7,4), ["exit_status"] = 0 },
        ["device"] = new JsonObject { ["protocol"] = protocol }, ["serial_number"] = "A", ["model_name"] = "model", ["scsi_model_name"] = "model",
        ["local_time"] = new JsonObject { ["time_t"] = new DateTimeOffset(Now).ToUnixTimeSeconds() }
    };
    private static SmartReportPreview Parse(JsonObject root) => SmartReports.Parse(new Disk { Serial="A", Model="model" },new(root.ToJsonString()),Now);
    [Theory]
    [InlineData("ATA","firmware_version","sata_version","string","SATA 3.3")]
    [InlineData("NVMe","firmware_version","nvme_version","string","1.4")]
    [InlineData("SCSI","scsi_revision","scsi_transport_protocol","name","SAS (SPL-4)")]
    public void ExplicitSourceFieldsRetainProvenanceAndAffectHash(string protocol,string firmware,string parent,string field,string value)
    {
        var root = Report(protocol); var missing = Parse(root);
        Assert.Null(missing.DeviceInfo!.Firmware); Assert.Null(missing.DeviceInfo.Interface); Assert.Equal("unknown",missing.DeviceInfo.Quality);
        root[firmware] = "FW01"; root[parent] = new JsonObject { [field] = value };
        var parsed = Parse(root);
        Assert.Equal("FW01",parsed.DeviceInfo!.Firmware); Assert.Equal(value,parsed.DeviceInfo.Interface);
        Assert.Equal(firmware,parsed.DeviceInfo.FirmwareField); Assert.Equal(parent+"."+field,parsed.DeviceInfo.InterfaceField);
        Assert.Equal("observed",parsed.DeviceInfo.Quality); Assert.NotEqual(missing.ReportHash,parsed.ReportHash);
        Assert.Equal(missing.BeforeDeviceInfoHash,parsed.BeforeDeviceInfoHash); Assert.Equal(missing.Readings,parsed.Readings);
        root[firmware] = "FW02"; Assert.NotEqual(parsed.ReportHash,Parse(root).ReportHash);
    }
    [Theory]
    [InlineData("null")][InlineData("22")][InlineData("{}")][InlineData("\"   \"")][InlineData("\"bad\\nvalue\"")]
    public void MalformedDeviceFieldsRemainUnknown(string json)
    {
        var root = Report("ATA"); root["firmware_version"] = JsonNode.Parse(json);
        root["sata_version"] = new JsonObject { ["string"] = JsonNode.Parse(json) };
        Assert.Null(Parse(root).DeviceInfo!.Firmware); Assert.Null(Parse(root).DeviceInfo!.Interface);
        Assert.Equal("unknown",Parse(root).DeviceInfo!.Quality);
    }
    [Theory][InlineData(true)][InlineData(false)]
    public void MissingTimeOrCommandFailurePreventsVerifiedEvidence(bool missingTime)
    {
        var root = Report("ATA"); root["firmware_version"] = "FW01";
        if (missingTime) root.Remove("local_time"); else root["smartctl"]!["exit_status"] = 4;
        Assert.Equal("unverified",Parse(root).DeviceInfo!.Quality);
    }
    [Fact] public void UnknownFieldsAndOlderReportsDoNotInventDeviceDetails()
    {
        var root=Report("SCSI");root["firmware_version"]="not the SCSI revision field";root["device"]!["type"]="sat";
        var parsed=Parse(root);Assert.Null(parsed.DeviceInfo!.Firmware);Assert.Null(parsed.DeviceInfo.Interface);
        var json=JsonNode.Parse(JsonSerializer.Serialize(parsed))!.AsObject();json.Remove("DeviceInfo");json.Remove("BeforeDeviceInfoHash");
        Assert.Null(JsonSerializer.Deserialize<SmartReportPreview>(json.ToJsonString())!.DeviceInfo);
        root["scsi_revision"]=new string('x',201);Assert.Null(Parse(root).DeviceInfo!.Firmware);
    }
}
