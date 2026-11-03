using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class DatasetRenames
{
    public static string Token(DatasetInventorySample from, DatasetInventorySample to) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { from, to })));
    public static string? Validate(DatasetInventorySample from, DatasetInventorySample to, DatasetRenameInput input)
    {
        if (from.ConnectionId != to.ConnectionId || from.Id == to.Id || from.FetchedUtc >= to.FetchedUtc)
            return "Choose an earlier and a later inventory from the same NAS.";
        if (string.IsNullOrWhiteSpace(from.SystemSerial) || from.SystemSerial != to.SystemSerial)
            return "Both inventories need the same known NAS system identity; do not infer a rename across unknown or changed sources.";
        if (input.RequestId == Guid.Empty || !input.ConfirmedSameDataset || string.IsNullOrWhiteSpace(input.Evidence) || input.Evidence.Length > 4000 || string.IsNullOrWhiteSpace(input.FromName) || input.FromName.Length > 256 || string.IsNullOrWhiteSpace(input.ToName) || input.ToName.Length > 256 || input.FromName == input.ToName || input.ConfirmedFromName != input.FromName || input.ConfirmedToName != input.ToName)
            return "Confirm both exact names, the same-dataset assertion and supporting evidence in 1–4000 characters.";
        DatasetSnapshot? oldSnapshot, newSnapshot;
        try
        {
            oldSnapshot = JsonSerializer.Deserialize<DatasetSnapshot>(from.SnapshotJson);
            newSnapshot = JsonSerializer.Deserialize<DatasetSnapshot>(to.SnapshotJson);
        }
        catch (JsonException)
        {
            return "The saved inventory cannot be interpreted.";
        }

        if (oldSnapshot?.Items is null || newSnapshot?.Items is null || oldSnapshot.Items.Length > 256 || newSnapshot.Items.Length > 256)
            return "Both saved inventories are required.";
        var oldRows = oldSnapshot.Items.Where(x => x.Name == input.FromName).ToArray();
        var newRows = newSnapshot.Items.Where(x => x.Name == input.ToName).ToArray();
        if (oldRows.Length != 1 || newRows.Length != 1 || oldRows[0].Type != newRows[0].Type || oldRows[0].Pool != newRows[0].Pool || oldSnapshot.Items.Any(x => x.Name == input.ToName) || newSnapshot.Items.Any(x => x.Name == input.FromName))
            return "Choose distinct non-coexisting names of the same type and pool. Conflicting inventory cannot establish this rename.";
        return null;
    }

    internal static DatasetRenamePreview Preview(DatasetInventorySample row) => new(row.Id, row.FetchedUtc, row.SystemSerial, row.Version, row.ConfigurationRevision, JsonSerializer.Deserialize<DatasetSnapshot>(row.SnapshotJson));
}
