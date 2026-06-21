using PUnit;
using Xunit;

namespace PUnit.Test.Resources;

public class SubjectAttributeTests
{
    [Fact]
    public void References_captures_subjects_and_defaults_to_empty()
    {
        Assert.Equal(["acc", "<return>"], new ReferencesAttribute("acc", Subject.Return).Subjects);
        Assert.Empty(new ReferencesAttribute().Subjects);
    }

    [Fact]
    public void Consumes_captures_subjects_and_defaults_to_empty()
    {
        Assert.Equal(["from"], new ConsumesAttribute("from").Subjects);
        Assert.Empty(new ConsumesAttribute().Subjects);
    }

    [Fact]
    public void Subject_Return_is_the_reserved_token()
    {
        Assert.Equal("<return>", Subject.Return);
    }
}
