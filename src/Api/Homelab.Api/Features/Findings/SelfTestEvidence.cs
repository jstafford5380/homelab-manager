
namespace Homelab.Api;
// A fingerprint identifies repeated reported evidence, not a physical test event.
public sealed class SelfTestEvidence
{
    public long Id { get; set; }
    public Guid DiskId { get; set; }
    public Guid ReportId { get; set; }
    public string Fingerprint { get; set; } = "";
}
