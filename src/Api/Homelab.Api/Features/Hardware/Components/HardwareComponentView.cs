namespace Homelab.Api;
public record HardwareComponentView(HardwareComponent Record, string Title, string Source, string Group, HardwareStatus Status, double? EnteredTotalBytes, HardwareSlot[] Slots, HardwareEvent[] History, HardwareMetric? Use = null, HardwareMetric? Heat = null);
