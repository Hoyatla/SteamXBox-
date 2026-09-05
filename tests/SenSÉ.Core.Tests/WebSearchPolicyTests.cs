using SenSÉ.Tools.Search;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// What an administrator fixes, and what the user may still change.
/// </summary>
/// <remarks>
/// Every case here is one a school or a company with sensitive data would refuse a deployment over.
/// </remarks>
public class WebSearchPolicyTests
{
    [Fact]
    public void WithNoPolicyNothingIsLocked()
    {
        var resolved = WebSearchPolicy.Resolve(new WebSearchSettings(), policy: null);

        Assert.Empty(resolved.Locked);
        Assert.False(resolved.IsLocked(WebSearchPolicy.ProviderField));
    }

    [Fact]
    public void WithNoUserSettingsTheDefaultsApply()
    {
        var resolved = WebSearchPolicy.Resolve(user: null, policy: null);

        Assert.Equal(WebSearchProvider.Browser, resolved.Effective.Provider);
        Assert.Equal(SafeSearch.Moderate, resolved.Effective.SafeSearch);
    }

    // A pupil who can switch safe search off is a deployment a secondary school refuses.
    [Fact]
    public void APolicyOverridesTheUserAndSaysSo()
    {
        var user = new WebSearchSettings(SafeSearch: SafeSearch.Off);
        var policy = new WebSearchPolicy.PolicyValues { SafeSearch = SafeSearch.Strict };

        var resolved = WebSearchPolicy.Resolve(user, policy);

        Assert.Equal(SafeSearch.Strict, resolved.Effective.SafeSearch);
        Assert.True(resolved.IsLocked(WebSearchPolicy.SafeSearchField));
    }

    // A user repointing the instance at the open internet is what a company with sensitive data
    // refuses.
    [Fact]
    public void TheInstanceAddressCanBeFixed()
    {
        var user = new WebSearchSettings(WebSearchProvider.Instance, "https://ailleurs.example");
        var policy = new WebSearchPolicy.PolicyValues { InstanceUrl = "https://recherche.interne" };

        var resolved = WebSearchPolicy.Resolve(user, policy);

        Assert.Equal("https://recherche.interne", resolved.Effective.InstanceUrl);
        Assert.True(resolved.IsLocked(WebSearchPolicy.InstanceUrlField));
    }

    [Fact]
    public void WebSearchCanBeTurnedOffAltogether()
    {
        var policy = new WebSearchPolicy.PolicyValues { Provider = WebSearchProvider.Disabled };

        var resolved = WebSearchPolicy.Resolve(new WebSearchSettings(), policy);

        Assert.Equal(WebSearchProvider.Disabled, resolved.Effective.Provider);
        Assert.True(resolved.IsLocked(WebSearchPolicy.ProviderField));
    }

    // Absence is what says "the user decides": an administrator forcing one field leaves the rest
    // alone rather than having to restate them.
    [Fact]
    public void WhatThePolicyDoesNotMentionStaysTheUsers()
    {
        var user = new WebSearchSettings(WebSearchProvider.Instance, "https://a.example", SafeSearch.Off);
        var policy = new WebSearchPolicy.PolicyValues { SafeSearch = SafeSearch.Strict };

        var resolved = WebSearchPolicy.Resolve(user, policy);

        Assert.Equal("https://a.example", resolved.Effective.InstanceUrl);
        Assert.False(resolved.IsLocked(WebSearchPolicy.InstanceUrlField));
        Assert.False(resolved.IsLocked(WebSearchPolicy.ProviderField));
    }

    // A launcher that cannot answer is worse than one that answers differently, and this happens
    // while an administrator is still writing the policy.
    [Fact]
    public void AnInstanceWithoutAnAddressFallsBackToTheBrowser()
    {
        var policy = new WebSearchPolicy.PolicyValues { Provider = WebSearchProvider.Instance };

        var resolved = WebSearchPolicy.Resolve(new WebSearchSettings(), policy);

        Assert.Equal(WebSearchProvider.Browser, resolved.Effective.Provider);

        // Still reported as locked: the administrator did fix it, and the interface must not offer
        // the choice back just because the address is missing.
        Assert.True(resolved.IsLocked(WebSearchPolicy.ProviderField));
    }

    [Fact]
    public void EveryFieldCanBeFixedAtOnce()
    {
        var policy = new WebSearchPolicy.PolicyValues
        {
            Provider = WebSearchProvider.Instance,
            InstanceUrl = "https://recherche.ecole",
            SafeSearch = SafeSearch.Strict,
        };

        var resolved = WebSearchPolicy.Resolve(new WebSearchSettings(), policy);

        Assert.Equal(3, resolved.Locked.Count);
        Assert.Equal(WebSearchProvider.Instance, resolved.Effective.Provider);
        Assert.Equal("https://recherche.ecole", resolved.Effective.InstanceUrl);
        Assert.Equal(SafeSearch.Strict, resolved.Effective.SafeSearch);
    }
}
