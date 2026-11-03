namespace Homelab.Api;

// Network operations manage short leases around database work, releasing them during remote calls.
public sealed record OwnsWriteLease;
