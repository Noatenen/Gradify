using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using AuthWithAdmin.Client;
using AuthWithAdmin.Client.ClientHelpers;
using AuthWithAdmin.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Dashboard, Tasks, Milestones & shared project context (cached per session)
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ITasksService, TasksService>();
builder.Services.AddScoped<IMilestonesService, MilestonesService>();
builder.Services.AddScoped<IProjectContextService, ProjectContextService>();

// Student project workspace (/project) — identity, resources, deliverables
builder.Services.AddScoped<IProjectWorkspaceService, ProjectWorkspaceService>();

// Resource files (admin/lecturer knowledge-base uploads)
builder.Services.AddScoped<IResourceFilesService, ResourceFilesService>();

// Learning materials (project/type-scoped materials for students)
builder.Services.AddScoped<ILearningMaterialsService, LearningMaterialsService>();

// Management area services
builder.Services.AddScoped<IManagementService, ManagementService>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<IMilestoneManagementService, MilestoneManagementService>();
builder.Services.AddScoped<ITaskManagementService, TaskManagementService>();
builder.Services.AddScoped<ITaskSubmissionsService, TaskSubmissionsService>();
builder.Services.AddScoped<IProjectRequestsService, ProjectRequestsService>();
builder.Services.AddScoped<IRequestTypesService, RequestTypesService>();
builder.Services.AddScoped<IRoadmapStagesService, RoadmapStagesService>();

// Student profile & settings
builder.Services.AddScoped<IStudentProfileService, StudentProfileService>();

// Universal user profile (shared /settings page for all roles)
builder.Services.AddScoped<IUserProfileService, UserProfileService>();

// Lecturer/admin "משימות ממתינות לאישור מנחה" inbox + reminder/override actions
builder.Services.AddScoped<IPendingMentorApprovalsService, PendingMentorApprovalsService>();

// Internal Project Health (traffic-light) — Admin/Staff/Mentor only
builder.Services.AddScoped<IProjectHealthService, ProjectHealthService>();

// Mentor preferences card on /settings (mentor-only)
builder.Services.AddScoped<IMentorPreferencesService, MentorPreferencesService>();

// Integration settings (admin-configurable OAuth credentials)
builder.Services.AddScoped<IIntegrationSettingsService, IntegrationSettingsService>();

// Per-user Google Calendar connection on /settings (connect / status / disconnect).
// Unrelated to Google LOGIN, which goes through AuthenticationService.
builder.Services.AddScoped<IGoogleCalendarService, GoogleCalendarService>();

// Mentor area
builder.Services.AddScoped<IMentorProjectsService, MentorProjectsService>();
// The single source of truth for "what needs my attention" — one GET whose
// answer (waiting age, aging bucket, ordering, counts) is computed entirely
// server-side and shared with the daily digest.
builder.Services.AddScoped<IMentorAttentionService, MentorAttentionService>();
// Cross-project snapshot shared by בית, המשימות שלי and יומן ותכנון. Composes
// the services around it — it owns no endpoint of its own.
builder.Services.AddScoped<IMentorWorkspaceService, MentorWorkspaceService>();

// In-system notifications
builder.Services.AddScoped<INotificationService, NotificationService>();

// Student assignment / preference form
builder.Services.AddScoped<IAssignmentService, AssignmentService>();

// Lecturer/Admin assignment-form management (review submissions)
builder.Services.AddScoped<IAssignmentManagementService, AssignmentManagementService>();

// Reusable form-builder system
builder.Services.AddScoped<IFormsManagementService, FormsManagementService>();

// Airtable per-academic-year integration management
builder.Services.AddScoped<IAirtableIntegrationService, AirtableIntegrationService>();

// Permission catalog + per-user cache (legacy keyed system)
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IPermissionsManagementService, PermissionsManagementService>();

// RoleSettings — simple feature-flag matrix (independent surface for new pages)
builder.Services.AddScoped<IRoleFeatureService, RoleFeatureService>();
builder.Services.AddScoped<IRoleSettingsManagementService, RoleSettingsManagementService>();

// Lecturer / Mentor dashboard
builder.Services.AddScoped<IDashboardOverviewService, DashboardOverviewService>();

// Lecturer Home workspace — composes existing dashboard + requests + pending
// approvals endpoints into one snapshot, same pattern as MentorWorkspaceService.
builder.Services.AddScoped<ILecturerHomeService, LecturerHomeService>();

// Lecturer / Mentor milestones overview
builder.Services.AddScoped<IMilestonesOverviewService, MilestonesOverviewService>();

// Lecturer / Mentor project-detail mini dashboard
builder.Services.AddScoped<IProjectOverviewService, ProjectOverviewService>();

// User-level notification preferences (settings → העדפות התראות)
builder.Services.AddScoped<INotificationPreferencesService, NotificationPreferencesService>();

//User management
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<AuthenticationStateProvider, AuthStateProvider>();
builder.Services.AddScoped<ILocalStorageService, LocalStorageService>();
// Login-form preference ("זכור אותי"): an email and a boolean. Never a password.
builder.Services.AddScoped<IRememberedLoginService, RememberedLoginService>();
builder.Services.AddScoped<UserContextService>();
builder.Services.AddScoped<IPersonalTasksService, PersonalTasksService>();
builder.Services.AddScoped<ITeamTasksService, TeamTasksService>();

builder.Services.AddAuthorizationCore(options =>
{
    options.AddPolicy("VerifiedUser", policy =>
        policy.RequireClaim("IsVerified", "true"));
});

await builder.Build().RunAsync();