namespace Homelab.Api.Features.Storage.Drives.PreviewIdentityMerge;
public sealed record ResponseRecordsItem(Guid Id, string Serial, string Model, string? Wwn, string MappingConfidence, bool Present, string? Bay, bool BayVerified);
public sealed record Response(string ReviewToken, string FirstIdentity, string SecondIdentity, IEnumerable<ResponseRecordsItem> Records, string Explanation);
