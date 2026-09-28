using ColdCeph.Core.Features.S3.DTOs;

namespace ColdCeph.Core.Tests.Features.S3.Unit;

[TestFixture]
public sealed class S3KeyRulesTests
{
    [Test]
    public void FileName_uses_the_last_segment()
    {
        Assert.That(S3KeyRules.FileName("photos/2024/cat.jpg"), Is.EqualTo("cat.jpg"));
        Assert.That(S3KeyRules.FileName("photos/"), Is.EqualTo("photos"));
        Assert.That(S3KeyRules.FileName("readme.txt"), Is.EqualTo("readme.txt"));
    }
}
