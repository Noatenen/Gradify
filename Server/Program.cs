using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.Google;
using System.Text;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
var builder = WebApplication.CreateBuilder(args);
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port) && !builder.Environment.IsDevelopment())
{
    builder.WebHost.UseUrls($"http://*:{port}");
}
StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);

// Add services to the container.

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

//DB
builder.Services.AddScoped<DbRepository>();

//User management
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<AuthCheck>();
builder.Services.AddScoped<AuthRepository>();
builder.Services.AddScoped<ITokenBlacklistService, DbTokenBlacklistService>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddHostedService<TokenCleanupBackgroundService>();

//Files
builder.Services.AddScoped<FilesManage>();

//Airtable
builder.Services.AddHttpClient("Airtable");
builder.Services.AddScoped<AirtableService>();

//Slack
builder.Services.AddHttpClient("Slack");
builder.Services.Configure<SlackOptions>(
    builder.Configuration.GetSection(SlackOptions.SectionName));

//Google Calendar — per-user OAuth connection.
//
// Separate from the Google LOGIN handler registered further down: different
// routes, different redirect URI, different scopes, its own storage. The two
// only share the ONE set of client credentials, read straight from
// Authentication:Google:* by GoogleCalendarTokenService rather than copied into
// a second config section.
builder.Services.AddHttpClient(GoogleCalendarTokenService.HttpClientName);
builder.Services.Configure<GoogleCalendarOptions>(
    builder.Configuration.GetSection(GoogleCalendarOptions.SectionName));
builder.Services.AddScoped<OAuthStateService>();
builder.Services.AddScoped<GoogleCalendarTokenService>();
// Calendar EVENT operations (task -> event). The only caller of the Calendar
// events API; reuses the same named HttpClient and the token service above.
builder.Services.AddScoped<GoogleCalendarEventService>();

// Data Protection — encrypts the Google refresh/access tokens before they reach
// SQLite. Keys are pinned to an explicit directory instead of the framework
// default (~/.aspnet/DataProtection-Keys), because that default is per-user and
// per-container: a redeploy would silently lose the key ring and every stored
// refresh token would become undecryptable. SetApplicationName keeps the ring
// stable across environments. In a container this directory MUST be on a
// persistent volume.
var dataProtectionKeys = builder.Configuration["DataProtection:KeysDirectory"];
if (string.IsNullOrWhiteSpace(dataProtectionKeys))
    dataProtectionKeys = Path.Combine(
        builder.Environment.ContentRootPath, "App_Data", "DataProtection-Keys");

Directory.CreateDirectory(dataProtectionKeys);

builder.Services.AddDataProtection()
    .SetApplicationName("Gradify")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeys));

//Mail
builder.Services.AddSingleton<EmailHelper>();

//Mentor attention + daily digest
// Scoped, because both depend on the scoped DbRepository (one SqliteConnection
// per instance). The background scheduler resolves them inside its own scope
// per run — never from the root provider.
builder.Services.AddScoped<MentorAttentionService>();
builder.Services.AddScoped<MentorDigestService>();
builder.Services.AddHostedService<MentorDigestBackgroundService>();


//JWT
var jwtSettings = builder.Configuration.GetSection("JWTSettings");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    // Challenge with JWT Bearer (returns 401) — NOT Google. The interactive
    // Google login is triggered explicitly via UsersController.Google ->
    // Challenge(GoogleDefaults.AuthenticationScheme), so it does not depend on
    // this default. Previously the default was Google, which meant any API call
    // that arrived without a valid Bearer token got a 302 redirect to
    // accounts.google.com instead of a 401. The Blazor HttpClient silently
    // followed that cross-origin redirect, GetFromJsonAsync then failed to parse
    // the Google HTML, and the dashboard surfaced a generic "load failed" error
    // with NO obvious failed request in the Network tab. A clean 401 makes the
    // real auth failure explicit (and the client retry/diagnostics can see it).
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme; // Required for Google sign-in
})
.AddCookie() // Required for external authentication like Google
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.GetValue<string>("validIssuer"),
        ValidAudience = jwtSettings.GetValue<string>("validIssuer"),
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.GetValue<string>("securityKey")))
    };
})
.AddGoogle(options =>
{
    options.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
    options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;

    options.Events = new OAuthEvents
    {
        OnRemoteFailure = context =>
        {
            context.HandleResponse(); // מונע 500

            var page =
                context.Properties?.Items.TryGetValue("page", out var p) == true
                    ? p
                    : "./";

            context.Response.Redirect(page);
            return Task.CompletedTask;
        }
    };
});


builder.Services.AddAuthorization(); // Add Authorization services


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();


var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".data"] = "applocation/json";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = provider
});

app.UseRouting();

//user management
app.UseAuthentication();
app.UseMiddleware<TokenBlacklistMiddleware>();
app.UseAuthorization();


app.MapRazorPages();
app.MapControllers();
app.MapFallbackToFile("index.html");

await DatabaseMigrator.MigrateAsync(app.Configuration);

app.Run();

