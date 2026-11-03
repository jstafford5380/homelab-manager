
namespace Homelab.Api;
public record DatasetRenameInput(long FromSampleId, long ToSampleId, string FromName, string ToName, string ConfirmedFromName, string ConfirmedToName, string ReviewToken, string Evidence, bool ConfirmedSameDataset, Guid RequestId);
