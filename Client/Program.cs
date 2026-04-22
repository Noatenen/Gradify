using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using AuthWithAdmin.Client;
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

//User management
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<AuthenticationStateProvider, AuthStateProvider>();
builder.Services.AddScoped<ILocalStorageService, LocalStorageService>();
builder.Services.AddScoped<UserContextService>();

builder.Services.AddAuthorizationCore(options =>
{
    options.AddPolicy("VerifiedUser", policy =>
        policy.RequireClaim("IsVerified", "true"));
});

await builder.Build().RunAsync();