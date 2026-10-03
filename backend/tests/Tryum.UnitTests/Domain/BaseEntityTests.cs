using Tryum.Domain.Common;

namespace Tryum.UnitTests.Persistence;

public class BaseEntityTests
{
    [Fact]
    public void NewEntity_ShouldHaveNonEmptyGuid()
    {
        var entity = new Sample();

        Assert.NotEqual(Guid.Empty, entity.Id);
    }

    [Fact]
    public void TwoEntities_ShouldHaveDifferentIds()
    {
        var a = new Sample();
        var b = new Sample();

        Assert.NotEqual(a.Id, b.Id);
    }

    private sealed class Sample : BaseEntity;
}
