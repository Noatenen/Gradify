using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

/// <summary>
/// The client-facing view of a user's Google Calendar connection.
///
/// Deliberately minimal: it says whether a real OAuth grant exists, which
/// Google account it belongs to, and when it was made. It never carries an
/// access token, a refresh token, a scope list or an error detail — the client
/// has no use for any of those and they must not cross the wire.
///
/// The source of truth is the GoogleCalendarConnections row, NOT the legacy
/// UserPreferences.GoogleCalendarConnected flag (which is only a checkbox the
/// user could tick without ever authorizing anything).
/// </summary>
public class GoogleCalendarStatusDto
{
    public bool    IsConnected { get; set; }

    /// <summary>Google account the grant belongs to. Null when not connected,
    /// or when the grant was made without the identity scopes that reveal it.</summary>
    public string? GoogleEmail { get; set; }

    /// <summary>UTC timestamp of the most recent successful authorization.</summary>
    public string? ConnectedAt { get; set; }
}

/// <summary>Authorization URL the client navigates to in order to start consent.</summary>
public class GoogleCalendarConnectUrlDto
{
    public string Url { get; set; } = "";
}
