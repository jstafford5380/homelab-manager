
namespace Homelab.Api;
public sealed class DeliveryAttempt
{
    public long Id { get; set; }
    public Guid DeliveryId { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public int? HttpStatus { get; set; }
    public string Result { get; set; } = "";
}
