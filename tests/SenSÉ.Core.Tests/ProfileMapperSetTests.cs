using SenSÉ.Core.Mapping;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// One profile mapper per controller.
/// </summary>
/// <remarks>
/// A mapper detects button edges by comparing each frame to the one before it. Shared between two
/// controllers, that comparison happens across them and every button is pressed and released dozens
/// of times a second — a pad that works for a moment and then does something arbitrary.
/// </remarks>
public class ProfileMapperSetTests
{
    private const string PadA = "hid:pad-a";
    private const string PadB = "xinput-slot:1";

    private static (ProfileMapperSet Set, List<string> Built) Build()
    {
        var built = new List<string>();
        var set = new ProfileMapperSet(id =>
        {
            built.Add(id);
            return new ProfileMapper();
        });

        return (set, built);
    }

    // The point. Two controllers must never share the memory of what was pressed.
    [Fact]
    public void TwoControllersGetTwoMappers()
    {
        var (set, _) = Build();

        Assert.NotSame(set.For(PadA), set.For(PadB));
        Assert.Equal(2, set.Count);
    }

    [Fact]
    public void OneControllerAlwaysGetsTheSameMapper()
    {
        var (set, built) = Build();

        Assert.Same(set.For(PadA), set.For(PadA));
        Assert.Single(built);
    }

    // The id reaches the factory so each controller can be built from its own profile rather than
    // one profile serving everybody.
    [Fact]
    public void TheFactoryIsToldWhichControllerItIsBuildingFor()
    {
        var (set, built) = Build();

        set.For(PadB);

        Assert.Equal([PadB], built);
    }

    [Fact]
    public void NoMapperExistsBeforeAControllerSendsSomething()
    {
        var (set, built) = Build();

        Assert.Equal(0, set.Count);
        Assert.False(set.Has(PadA));
        Assert.Empty(built);
    }

    // A pad that leaves mid-press would come back to a mapper still believing that button is held.
    [Fact]
    public void AControllerThatLeavesLosesItsMemory()
    {
        var (set, built) = Build();

        var first = set.For(PadA);
        set.Forget(PadA);

        Assert.False(set.Has(PadA));
        Assert.NotSame(first, set.For(PadA));
        Assert.Equal(2, built.Count);
    }

    [Fact]
    public void ForgettingAnUnknownControllerIsHarmless()
    {
        var (set, _) = Build();

        set.Forget("never-seen");

        Assert.Equal(0, set.Count);
    }

    [Fact]
    public void ReloadingRebuildsEveryMapperInPlace()
    {
        var (set, built) = Build();

        var before = set.For(PadA);
        set.For(PadB);
        set.Reload();

        Assert.Equal(2, set.Count);
        Assert.NotSame(before, set.For(PadA));
        Assert.Equal(4, built.Count);
    }

    [Fact]
    public void ReloadingNothingIsHarmless()
    {
        var (set, built) = Build();

        set.Reload();

        Assert.Equal(0, set.Count);
        Assert.Empty(built);
    }
}
