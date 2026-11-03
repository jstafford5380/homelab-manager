namespace Homelab.Api;
public static class ConnectionRules
{
    public static string? ValidateApiKey(string? key) => TrueNasConnectionSettings.ValidateApiKey(key);
    public static string? Validate(ConnectionSettings input) => ValidateField(input)?.Message;
    public static (string Field, string Message)? ValidateField(ConnectionSettings input) =>
        TrueNasConnectionSettings.ValidateField(input);
}
