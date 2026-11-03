
namespace Homelab.Api;
public record VdevCounterRange(long KnownCount, long? Min, long? Max)
{
    public VdevCounterRange Add(long? value) => value is null ? this : new(KnownCount + 1, Min is { } min ? Math.Min(min, value.Value) : value, Max is { } max ? Math.Max(max, value.Value) : value);
}
