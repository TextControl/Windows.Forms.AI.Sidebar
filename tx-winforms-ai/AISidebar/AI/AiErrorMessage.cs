namespace TXTextControl.AISidebar.AI;

/// <summary>Produces user-facing failures with configured secrets removed.</summary>
internal static class AiErrorMessage
{
    /// <summary>Formats a provider failure while redacting keys and other supplied secrets.</summary>
    public static string For(Exception exception, params string[] secrets)
    {
        var message = exception.Message;
        foreach (var secret in secrets)
            if (!string.IsNullOrEmpty(secret)) message = message.Replace(secret, "[hidden]", StringComparison.Ordinal);
        return message.Length <= 700 ? message : message[..700] + "…";
    }
}
