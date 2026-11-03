
namespace Homelab.Api;
public record VdevDriveMapping(string Status, Guid? DiskId, string? Serial, string? Wwn, string? Model, string Explanation);
