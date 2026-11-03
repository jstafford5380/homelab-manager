
namespace Homelab.Api;
public record VdevComparison(string Status, string Explanation, DateTime? PreviousFetchedUtc, VdevMemberChange[] Members);
