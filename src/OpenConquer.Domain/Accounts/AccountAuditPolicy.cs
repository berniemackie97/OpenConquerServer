namespace OpenConquer.Domain.Accounts;

public static class AccountAuditPolicy
{
    public const int MaximumReasonCodeLength = 64;

    public static bool IsValidReasonCode(string? reasonCode)
    {
        if (reasonCode is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(reasonCode) || reasonCode.Length > MaximumReasonCodeLength)
        {
            return false;
        }

        foreach (char character in reasonCode)
        {
            if (character is < ' ' or > '~')
            {
                return false;
            }
        }

        return true;
    }
}
