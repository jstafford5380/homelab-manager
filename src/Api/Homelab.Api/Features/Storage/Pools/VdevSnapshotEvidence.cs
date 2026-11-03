
namespace Homelab.Api;
public record VdevSnapshotEvidence(string Schema, string Status, string Explanation, VdevNodeEvidence[] Nodes);
