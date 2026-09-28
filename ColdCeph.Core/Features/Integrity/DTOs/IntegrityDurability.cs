namespace ColdCeph.Core.Features.Integrity.DTOs;

public static class IntegrityDurability
{
    public static bool IsFailure(string text)
        => Contains(text, "unfound")
           || Contains(text, "inconsistent")
           || Contains(text, "incomplete");

    public static bool HasFailure(IntegritySnapshot snapshot)
        => snapshot.Checks.Any(check => IsFailure(check.Name) || IsFailure(check.Detail));

    private static bool Contains(string text, string token)
        => text.Contains(token, StringComparison.OrdinalIgnoreCase);
}
