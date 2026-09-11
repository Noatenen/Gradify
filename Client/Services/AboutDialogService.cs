namespace AuthWithAdmin.Client.Services;

/// <summary>
/// The one switch that opens "אודות".
///
/// <para><b>Why a service and not a parameter.</b> The dialog now has two
/// entry points that sit in different parts of the tree — the profile menu
/// inside MotivaTopNav / AppSideNav, and the footer line — and neither is an
/// ancestor of the other. Threading a callback from AppLayout down through
/// the nav would mean every component in between carrying a parameter it has
/// no interest in, and mounting a second &lt;AboutModal&gt; beside the menu
/// would mean two dialogs and two pieces of open-state that could disagree.</para>
///
/// <para>So AppLayout mounts the dialog ONCE and listens here; the entry
/// points call <see cref="Open"/> and know nothing else. Adding a third entry
/// point later costs one call.</para>
///
/// <para>Scoped, like every other client service: in a WebAssembly host that
/// is the lifetime of the app, so all consumers share one instance.</para>
/// </summary>
public sealed class AboutDialogService
{
    /// <summary>Raised when something asks for the dialog. AppLayout is the
    /// only subscriber — it owns the single modal instance.</summary>
    public event Action? OpenRequested;

    public void Open() => OpenRequested?.Invoke();
}
