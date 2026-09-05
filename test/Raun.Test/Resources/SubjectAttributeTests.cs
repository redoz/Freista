using Raun;
using Xunit;

namespace Raun.Test.Resources;

public class SubjectAttributeTests
{
    [Fact]
    public void Created_lineage_properties_default_to_empty()
    {
        Assert.Empty(new CreatedAttribute().References);
        Assert.Empty(new CreatedAttribute().Consumes);
    }

    [Fact]
    public void Created_captures_references_and_consumes()
    {
        var attr = new CreatedAttribute { References = ["user", Subject.Return], Consumes = ["slot"] };
        Assert.Equal(["user", "<return>"], attr.References);
        Assert.Equal(["slot"], attr.Consumes);
    }

    [Fact]
    public void Edited_carries_the_same_lineage_surface()
    {
        var attr = new EditedAttribute { References = ["who"] };
        Assert.Equal(["who"], attr.References);
        Assert.Empty(attr.Consumes);
    }

    [Fact]
    public void Subject_Return_is_the_reserved_token() => Assert.Equal("<return>", Subject.Return);
}
