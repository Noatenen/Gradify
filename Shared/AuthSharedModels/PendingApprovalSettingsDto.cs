using System;
using System.Collections.Generic;

namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Pending-approval settings — drives the operational queue at
//  /management/pending-mentor-approvals (reminder thresholds, channels,
//  recipients, escalation, lecturer overrides, automation).
//
//  Singleton row on the server side (Id = 1). The configuration page reads
//  the entire object, mutates it client-side, and PUTs the whole DTO back —
//  no patch semantics, no field-level concurrency.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Allowed values for <see cref="PendingApprovalSettingsDto.ReminderFrequency"/>.</summary>
public static class ReminderFrequencies
{
    public const string Once         = "Once";          // single reminder when threshold hits
    public const string Daily        = "Daily";         // every day until handled
    public const string EveryNDays   = "EveryNDays";    // interval driven by ReminderIntervalDays
    public const string Weekdays     = "Weekdays";      // mon–thu only (no weekend reminders)

    public static readonly IReadOnlyList<string> All =
        new[] { Once, Daily, EveryNDays, Weekdays };

    public static string Label(string s) => s switch
    {
        Once       => "תזכורת חד-פעמית",
        Daily      => "כל יום",
        EveryNDays => "כל N ימים",
        Weekdays   => "ימי חול בלבד (א'–ה')",
        _          => s,
    };
}

public class PendingApprovalSettingsDto
{
    // ── Response-time thresholds (days) ────────────────────────────────────
    public int  WarningAfterDays    { get; set; } = 3;
    public int  EscalationAfterDays { get; set; } = 7;
    public int  CriticalAfterDays   { get; set; } = 14;

    // ── Channels ──────────────────────────────────────────────────────────
    public bool ChannelEmail        { get; set; } = true;
    public bool ChannelInSystem     { get; set; } = true;
    /// <summary>Reserved for future Slack integration — UI toggle is live
    /// but the channel doesn't dispatch yet. Saved alongside the rest.</summary>
    public bool ChannelSlack        { get; set; } = false;

    // ── Frequency ──────────────────────────────────────────────────────────
    /// <summary>One of <see cref="ReminderFrequencies"/>.</summary>
    public string ReminderFrequency    { get; set; } = ReminderFrequencies.Once;
    /// <summary>Interval in days when <see cref="ReminderFrequency"/> = EveryNDays.</summary>
    public int    ReminderIntervalDays { get; set; } = 1;

    // ── Recipients ─────────────────────────────────────────────────────────
    public bool RecipientMentor      { get; set; } = true;
    public bool RecipientLecturer    { get; set; } = false;
    public bool RecipientCoordinator { get; set; } = false;
    public bool RecipientTeam        { get; set; } = false;

    // ── Escalation behaviour ──────────────────────────────────────────────
    public bool EscalateNotifyLecturer    { get; set; } = true;
    public bool EscalateNotifyCoordinator { get; set; } = false;
    public bool MarkProjectAtRisk         { get; set; } = true;
    public bool ShowInManagementDashboard { get; set; } = true;

    // ── Lecturer override capabilities ────────────────────────────────────
    public bool LecturerCanApproveWithoutMentor { get; set; } = true;
    public bool LecturerCanRejectWithoutMentor  { get; set; } = false;
    public bool LecturerCanReopenSubmissions    { get; set; } = true;
    public bool LecturerCanForceMilestone       { get; set; } = false;

    // ── Automation toggles ─────────────────────────────────────────────────
    public bool AutoEscalation           { get; set; } = true;
    public bool AutoProjectHealthUpdates { get; set; } = true;
    public bool AutoReminderScheduling   { get; set; } = true;
    public bool AutoNotificationCleanup  { get; set; } = false;

    // ── Audit (read-only on the wire) ─────────────────────────────────────
    public DateTime? UpdatedAt       { get; set; }
    public int?      UpdatedByUserId { get; set; }
    public string    UpdatedByName   { get; set; } = "";
}