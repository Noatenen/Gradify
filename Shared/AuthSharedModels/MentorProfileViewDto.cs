using System;
using System.Collections.Generic;

namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Mentor Profile (read-only) — what students / teammates see when they click
//  on a mentor name. Mirrors MentorPreferencesDto for the *expectation-setting*
//  fields only.
//
//  Deliberately omitted to keep students unaware of the internal Project Health
//  monitoring system:
//    • PreferenceEvents     (contains tokens like "delays", "no_response")
//    • PreferenceFrequency  (digest cadence used by the staff/lecturer flow)
//
//  All other mentor preferences are expectation-setting and safe to expose:
//  communication preferences, availability, review iterations, feedback style,
//  red lines, etc. — the kind of thing students *should* know about their
//  mentor.
// ─────────────────────────────────────────────────────────────────────────────

public class MentorProfileViewDto
{
    public int      UserId    { get; set; }
    public string   FirstName { get; set; } = "";
    public string   LastName  { get; set; } = "";
    public string?  Email     { get; set; }

    // 1. Mentorship style
    public string?       RoleDescription                  { get; set; }
    public string?       InvolvementLevel                 { get; set; }

    // 2. Communication preferences
    public string?       PreferredChannel                 { get; set; }
    public string?       PreferredChannelOther            { get; set; }
    public string?       ExpectedResponseTime             { get; set; }
    public string?       CommunicationFrequency           { get; set; }

    // 3. Student updates
    public bool          RequirePeriodicUpdates           { get; set; }
    public string?       UpdateFrequency                  { get; set; }
    public List<string>  UpdateContent                    { get; set; } = new();

    // 4. Client interaction
    public string?       ClientInteractionFrequency       { get; set; }
    public string?       ClientInteractionFrequencyCustom { get; set; }
    public string?       MentorInClientInteraction        { get; set; }

    // 5. Submission & feedback
    public string?       SubmissionLeadTime               { get; set; }
    public string?       SubmissionLeadTimeCustom         { get; set; }
    public int?          ReviewIterations                 { get; set; }
    public string?       FeedbackType                     { get; set; }

    // 6. Decision involvement
    public List<string>  DecisionInvolvement              { get; set; } = new();

    // 7. Quality expectations
    public string?       QualityDescription               { get; set; }
    public List<string>  QualityFocusAreas                { get; set; } = new();

    // 8. Red lines
    public string?       RedLines                         { get; set; }

    // 9. Availability
    public string?       AvailableTimes                   { get; set; }
    public string?       MeetingFormat                    { get; set; }
    public string?       SchedulingMethod                 { get; set; }
}
