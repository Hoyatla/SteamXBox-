using Sc2Xboxed.Core.Input;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Each family numbers its profiles from one, and a deleted number is refilled.
/// </summary>
/// <remarks>
/// The number is a position the user counts to, not a serial. "One more than the largest" would
/// leave a hole for good after any deletion — 1, 3, 4 — and from then on the number tells the user
/// nothing about where the profile sits in their own list.
/// </remarks>
public class ProfileNumberingTests
{
	[Fact]
	public void TheFirstProfileOfAFamilyIsOne()
	{
		Assert.Equal(1, ProfileNumbering.NextFree([]));
		Assert.Equal("Manette PS5 1", ProfileNumbering.NextName("Manette PS5", []));
	}

	[Fact]
	public void NumbersRunOnWhileNothingIsMissing()
	{
		Assert.Equal(2, ProfileNumbering.NextFree([1]));
		Assert.Equal(4, ProfileNumbering.NextFree([1, 2, 3]));
	}

	/// <summary>
	/// The hole comes first, and only then the largest plus one.
	/// </summary>
	[Fact]
	public void ADeletedNumberIsRefilledBeforeAnyNewOne()
	{
		// 1, 2, 3 with 2 deleted.
		Assert.Equal(2, ProfileNumbering.NextFree([1, 3]));

		// And once the hole is filled, counting resumes above the largest.
		Assert.Equal(4, ProfileNumbering.NextFree([1, 2, 3]));
	}

	[Fact]
	public void TheLowestHoleWinsWhenThereAreSeveral()
	{
		Assert.Equal(2, ProfileNumbering.NextFree([1, 3, 5, 6]));
	}

	/// <summary>
	/// Order, repeats and nonsense in the existing set change nothing.
	/// </summary>
	/// <remarks>
	/// This is fed from names a user can edit by hand, so it has to survive whatever comes back.
	/// </remarks>
	[Fact]
	public void TheSetIsTakenAsItComes()
	{
		Assert.Equal(3, ProfileNumbering.NextFree([2, 1, 2, 1]));
		Assert.Equal(1, ProfileNumbering.NextFree([0, -4]));
	}

	/// <summary>
	/// Families are numbered apart: two pads, both holding their family's first profile.
	/// </summary>
	[Fact]
	public void EachFamilyCountsOnItsOwn()
	{
		string[] all = ["Manette PS5 1", "Manette PS5 2", "Manette Xbox 1"];

		Assert.Equal("Manette PS5 3", ProfileNumbering.NextName("Manette PS5", all));
		Assert.Equal("Manette Xbox 2", ProfileNumbering.NextName("Manette Xbox", all));
		Assert.Equal("Steam Controller 1", ProfileNumbering.NextName("Steam Controller", all));
	}

	/// <summary>
	/// The name is built and read back by the same rule, or a profile exists twice or not at all.
	/// </summary>
	[Fact]
	public void ANameSurvivesTheRoundTrip()
	{
		for (var number = 1; number <= 30; number++)
		{
			var name = ProfileNumbering.NameFor("Manette PS5", number);
			Assert.Equal(number, ProfileNumbering.NumberOf("Manette PS5", name));
		}
	}

	/// <summary>
	/// A profile the user renamed holds no number, and does not make the next one skip.
	/// </summary>
	/// <remarks>
	/// Renaming is theirs to do. Such a profile must neither be renumbered behind their back nor
	/// count as occupying a slot it no longer names.
	/// </remarks>
	[Fact]
	public void ARenamedProfileHoldsNoNumber()
	{
		Assert.Null(ProfileNumbering.NumberOf("Manette PS5", "Course"));
		Assert.Null(ProfileNumbering.NumberOf("Manette PS5", "Manette PS5"));
		Assert.Null(ProfileNumbering.NumberOf("Manette PS5", "Manette PS5 deux"));
		Assert.Null(ProfileNumbering.NumberOf("Manette PS5", null));

		Assert.Equal("Manette PS5 1", ProfileNumbering.NextName("Manette PS5", ["Course", "Tir"]));
	}

	/// <summary>
	/// One family's name is not read as another's.
	/// </summary>
	[Fact]
	public void AnotherFamilysNameIsNotOurs()
	{
		Assert.Null(ProfileNumbering.NumberOf("Manette PS5", "Manette Xbox 2"));
		Assert.Null(ProfileNumbering.NumberOf("Manette Xbox", "Manette PS5 2"));
	}
}
