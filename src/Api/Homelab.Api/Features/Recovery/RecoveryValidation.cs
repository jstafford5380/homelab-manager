namespace Homelab.Api;
public static class RecoveryValidation
{
    private static bool Utc(DateTime value) => value.Kind == DateTimeKind.Utc && value >= DateTime.UnixEpoch;
    private static bool Text(string? value, int limit, bool required = true) => value is not null && value.Length <= limit && (!required || !string.IsNullOrWhiteSpace(value));
    private static bool Review(DateTime value, DateTime eventTime, DateTime now, bool submission) => Utc(value) && value > eventTime && (!submission || value > now && value <= now.AddYears(1));
    // Evaluation also checks historical records, but expiration is assessed separately and
    // a previously accepted review date is not reinterpreted relative to today's one-year limit.
    public static string? Run(BackupEvidence input, DateTime now, bool submission = true)
    {
        if (!Utc(input.AttemptedUtc) || input.AttemptedUtc > now || input.RecoveryPointUtc is { } point && (!Utc(point) || point > input.AttemptedUtc))
            return "Use explicit UTC attempt and recovery dates on or after January 1, 1970. The recovery point cannot follow the completed attempt, and the attempt cannot be in the future.";
        if (!Review(input.ReviewByUtc, input.AttemptedUtc, now, submission))
            return "Use an explicit UTC review date after the attempt and in the next year.";
        if (input.Outcome is not ("success" or "failed" or "unknown") || input.DestinationConfirmed && (input.RecoveryPointUtc is null || input.Outcome != "success"))
            return "Choose a supported outcome. Destination confirmation requires a successful attempt and its represented recovery point.";
        if (!Text(input.Evidence, 10000) || !Text(input.FailureReason, 10000, false))
            return "Provide evidence up to 10000 characters. Failure reason must be text up to 10000 characters; leave it empty if unknown.";
        return null;
    }

    public static string? Exercise(RestoreExercise input, DateTime now, bool submission = true)
    {
        if (!Utc(input.TestedUtc) || input.TestedUtc > now || !Utc(input.RecoveryPointUtc) || input.RecoveryPointUtc > input.TestedUtc)
            return "Use explicit UTC test and recovery dates on or after January 1, 1970. The recovery point cannot follow the completed test, and the test cannot be in the future.";
        if (!Review(input.ReviewByUtc, input.TestedUtc, now, submission))
            return "Use an explicit UTC review date after the test and in the next year.";
        if (input.Result is not ("passed" or "failed" or "aborted") || input.DurationMinutes < 1)
            return "Choose a supported result and a measured duration of at least one minute.";
        if (!Text(input.Scope, 10000) || !Text(input.Checks, 10000) || !Text(input.Evidence, 10000))
            return "Provide the restored scope, checks and evidence, each up to 10000 characters.";
        return null;
    }
}
