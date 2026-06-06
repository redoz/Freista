using System;
using System.Linq;
using PUnit;
using Xunit;

namespace PUnit.Test.Resources;

/// <summary>The role attributes exist with the right usage targets (return/parameter/method shorthand).</summary>
public class ResourceRoleAttributeTests
{
    static AttributeTargets Targets<T>() where T : Attribute =>
        typeof(T).GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>().Single().ValidOn;

    [Fact]
    public void Return_roles_allow_return_and_method()
    {
        Assert.True(Targets<CreatesAttribute>().HasFlag(AttributeTargets.ReturnValue));
        Assert.True(Targets<CreatesAttribute>().HasFlag(AttributeTargets.Method));
        Assert.True(Targets<LoadsAttribute>().HasFlag(AttributeTargets.ReturnValue));
        Assert.True(Targets<LoadsAttribute>().HasFlag(AttributeTargets.Method));
    }

    [Fact]
    public void Parameter_roles_allow_parameters()
    {
        Assert.True(Targets<ReadsAttribute>().HasFlag(AttributeTargets.Parameter));
        Assert.True(Targets<DeletesAttribute>().HasFlag(AttributeTargets.Parameter));
    }

    [Fact]
    public void Edits_is_valid_on_both_parameter_and_return()
    {
        var t = Targets<EditsAttribute>();
        Assert.True(t.HasFlag(AttributeTargets.Parameter));
        Assert.True(t.HasFlag(AttributeTargets.ReturnValue));
        Assert.True(t.HasFlag(AttributeTargets.Method));
    }
}
