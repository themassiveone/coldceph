namespace ColdCeph.Core.Features.Operations.DTOs;

public sealed record OperationId(string Value)
{
    public override string ToString() => Value;
}
