
namespace Homelab.Api;
public record VdevNodeEvidence(string Guid, string? ParentGuid, string Role, string Type, string? State, string? Path, string? Device, string? DiskAlias, long? ReadErrors, long? WriteErrors, long? ChecksumErrors, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] VdevDriveMapping? DriveMapping = null);
