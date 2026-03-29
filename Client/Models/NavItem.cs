using Microsoft.AspNetCore.Components.Routing;

namespace AuthWithAdmin.Client.Models;

/// <summary>A single sidebar navigation entry.</summary>
public record NavItem(
    string       Label,
    string       Href,
    string       Icon,
    NavLinkMatch Match = NavLinkMatch.Prefix
);
