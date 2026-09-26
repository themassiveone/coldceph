using ColdCeph.Core.Features.Operations.DTOs;

namespace ColdCeph.Core.Tests.Features.Operations.Unit;

[TestFixture]
public sealed class OperationIdRulesTests
{
    [Test]
    public void Create_emits_op_prefixed_uuid()
    {
        var id = OperationIdRules.Create();

        Assert.That(OperationIdRules.IsValid(id.Value), Is.True);
        Assert.That(id.Value, Does.StartWith("op-"));
    }

    [Test]
    public void Parse_rejects_empty_or_bare_uuid()
    {
        Assert.That(() => OperationIdRules.Parse(""), Throws.TypeOf<FormatException>());
        Assert.That(() => OperationIdRules.Parse(Guid.NewGuid().ToString("D")), Throws.TypeOf<FormatException>());
        Assert.That(OperationIdRules.IsValid(null), Is.False);
    }
}
