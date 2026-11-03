namespace Homelab.Api;
public sealed record PowerOnHoursRunResult(PowerOnHoursServerResult[] Servers);
public sealed record PowerOnHoursServerResult(string Name, bool Completed, int HoursAvailable, int Unsupported, int Denied, int Unavailable);
