namespace Homelab.Api;

public static class TemperaturePolicy
{
    public static string? Validate(OperatingSettings settings)
    {
        if (settings.TemperaturePolicyReason is null || settings.TemperaturePolicyReason.Length > 2000)
            return "Temperature policy basis must be at most 2,000 characters.";
        if (settings.TemperatureReviewCelsius is not {} limit) return null;
        if (!double.IsFinite(limit) || limit < -273.15 || limit > 1000)
            return "Enter a finite Celsius review limit between -273.15 and 1000, or leave it blank to disable.";
        return string.IsNullOrWhiteSpace(settings.TemperaturePolicyReason)
            ? "Record the basis for your local temperature policy before enabling it." : null;
    }

    public static RuleResult? Evaluate(Observation input, OperatingSettings settings)
    {
        if (Validate(settings) is not null || settings.TemperatureReviewCelsius is not {} limit ||
            input.Metric != "temperature" || input.Unit != "°C" || input.Quality != "observed" ||
            input.Value is not {} value || !double.IsFinite(value) || input.ObservedUtc is null || value <= limit) return null;
        return new("local-temperature-policy-v1", "review", "Reported temperature exceeded your local review limit",
            $"{value} °C observed at {input.ObservedUtc:O}; local review limit {limit} °C applied at import. Policy basis: {settings.TemperaturePolicyReason}",
            "Review current temperature evidence, workload and cooling against manufacturer guidance. This is a user-defined review policy, not a universal safe limit or a failure prediction. Later cooler readings do not automatically resolve this review.");
    }
}
