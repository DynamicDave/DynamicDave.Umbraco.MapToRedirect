using DynamicDave.Umbraco.MapToRedirect.Sites;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class InterfacePropertyTests
{
    // Mirrors Umbraco 18: the property lives on the base interface, the derived one declares nothing.
    public interface IMoved : IBaseVisible;

    public interface IBaseVisible
    {
        string Name { get; }
    }

    // Mirrors Umbraco 17: the property is declared directly on the interface.
    public interface IDirect
    {
        string Name { get; }
    }

    private sealed class Implementation(string name) : IMoved, IDirect
    {
        public string Name => name;
    }

    [Fact]
    public void Reads_a_property_declared_on_a_base_interface()
    {
        var getter = InterfaceProperty.Getter<IMoved, string>("Name");

        Assert.Equal("Home", getter(new Implementation("Home")));
    }

    [Fact]
    public void Reads_a_property_declared_directly_on_the_interface()
    {
        var getter = InterfaceProperty.Getter<IDirect, string>("Name");

        Assert.Equal("Team", getter(new Implementation("Team")));
    }

    [Fact]
    public void Throws_for_an_unknown_property() =>
        Assert.Throws<MissingMemberException>(() => InterfaceProperty.Getter<IMoved, string>("Nope"));

    [Fact]
    public void Throws_when_the_property_type_does_not_match() =>
        Assert.Throws<MissingMemberException>(() => InterfaceProperty.Getter<IMoved, int>("Name"));
}
