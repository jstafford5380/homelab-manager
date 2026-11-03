namespace Homelab.Api.Features.Recovery.ListPolicies;
public sealed record ResponseItem(RecoveryPolicy Policy, RecoveryAssessment Assessment, IEnumerable<BackupEvidence> Runs, IEnumerable<RestoreExercise> Exercises);
