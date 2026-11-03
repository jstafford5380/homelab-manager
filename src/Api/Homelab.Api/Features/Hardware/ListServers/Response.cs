namespace Homelab.Api.Features.Hardware.ListServers;
public sealed record Response(bool Hwmetrics, bool Synology, List<HardwareServerView> Servers);
