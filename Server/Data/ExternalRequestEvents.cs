using System;
using System.Threading.Tasks;

namespace AuthWithAdmin.Server.Data;

// ─────────────────────────────────────────────────────────────────────────────
//  ExternalRequestEvents — in-process pub/sub for the Innovation-Team
//  integration. Static-only by design: this is *infrastructure prep* for
//  notification work; no real subscribers exist yet. Once the notification
//  pipeline lands, subscribers attach to these events in startup wiring
//  (anywhere in the server) without changing the controllers that raise them.
//
//  Why static (not a DI service)?
//    • Keeps Program.cs untouched — no new registrations required to ship
//      this groundwork.
//    • Subscribers are typically global side-effects (notifications, audit,
//      Slack relays); a per-request scoped service offers no advantage.
//    • Each handler is responsible for opening its own DI scope when it
//      needs scoped services (e.g. DbRepository).
//
//  Failure isolation: each subscriber is awaited in a try/catch so one bad
//  handler can't block the others or fail the inbound webhook write.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Read-only context attached to every external-request lifecycle event.
/// </summary>
public record ExternalRequestStatusChanged(
    string  ExternalRequestId,
    string  OldStatus,
    string  NewStatus,
    string  OldStatusLabel,
    string  NewStatusLabel,
    int?    StudentId,
    string  StudentEmail,
    int?    ProjectId,
    string  RequestType,
    string  Notes);

public record ExternalRequestCreated(
    string  ExternalRequestId,
    string  Status,
    string  StatusLabel,
    int?    StudentId,
    string  StudentEmail,
    int?    ProjectId,
    string  RequestType,
    string  Notes);

public static class ExternalRequestEvents
{
    /// <summary>Raised after the inbound upsert when a row already existed
    /// and the Status column changed (case-insensitive, trimmed compare).</summary>
    public static event Func<ExternalRequestStatusChanged, Task>? StatusChanged;

    /// <summary>Raised after the inbound upsert created a new row.</summary>
    public static event Func<ExternalRequestCreated, Task>? Created;

    public static async Task RaiseStatusChangedAsync(ExternalRequestStatusChanged ev)
    {
        if (StatusChanged is null) return;
        foreach (Func<ExternalRequestStatusChanged, Task> handler in StatusChanged.GetInvocationList())
        {
            try { await handler(ev); }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[ExternalRequestEvents.StatusChanged] handler failed: {ex.Message}");
            }
        }
    }

    public static async Task RaiseCreatedAsync(ExternalRequestCreated ev)
    {
        if (Created is null) return;
        foreach (Func<ExternalRequestCreated, Task> handler in Created.GetInvocationList())
        {
            try { await handler(ev); }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[ExternalRequestEvents.Created] handler failed: {ex.Message}");
            }
        }
    }
}