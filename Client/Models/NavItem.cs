using Microsoft.AspNetCore.Components.Routing;

namespace AuthWithAdmin.Client.Models;

/// <summary>A single sidebar navigation entry.</summary>
/// <param name="Icon">Open-iconic modifier class, e.g. "oi-folder". Still the
/// icon every non-student role renders, unchanged.</param>
/// <param name="IconPaths">Optional SVG path data (24x24 viewBox), drawn
/// INSTEAD of <paramref name="Icon"/> in the student rail.
///
/// <para>Open-iconic is a font: its glyphs are filled, drawn at different
/// optical weights, and several of them (oi-dashboard worst of all) do not
/// read as the page they point at. The rest of the Motiva student surface is
/// stroked 24x24 line art, so the student nav is too. Supplying this is opt-in
/// per item — an item without it renders the open-iconic glyph exactly as
/// before, which is what keeps Mentor / Lecturer / Admin untouched.</para></param>
public record NavItem(
    string                  Label,
    string                  Href,
    string                  Icon,
    NavLinkMatch            Match     = NavLinkMatch.Prefix,
    IReadOnlyList<string>?  IconPaths = null
);
