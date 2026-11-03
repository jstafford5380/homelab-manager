namespace Homelab.Api;

public interface IIntegrationProvider
{
    string ProviderId { get; }
}

public sealed class ProviderRegistry(IEnumerable<IIntegrationProvider> providers)
{
    private readonly IReadOnlyDictionary<string, IIntegrationProvider> registered =
        providers.ToDictionary(provider => provider.ProviderId, StringComparer.Ordinal);

    public T Resolve<T>(string id) where T : IIntegrationProvider =>
        registered.TryGetValue(id, out var provider) && provider is T capability
            ? capability
            : throw new InvalidOperationException($"No {typeof(T).Name} provider is registered for '{id}'.");
}
