using System.Text.RegularExpressions;

namespace ColdCeph.Core.Features.Operations.DTOs;

public static partial class OperationIdRules
{
    [GeneratedRegex("^op-[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static OperationId Create() => new($"op-{Guid.NewGuid():D}");

    public static bool IsValid(string? value)
        => value is not null && Pattern().IsMatch(value);

    public static OperationId Parse(string value)
        => IsValid(value)
            ? new OperationId(value)
            : throw new FormatException("Operation IDs must match op-<uuid>.");
}
