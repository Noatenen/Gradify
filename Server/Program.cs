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
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
// ── Content root: the application directory, never the working directory ────
//
// ASP.NET Core's default content root is Directory.GetCurrentDirectory(), i.e.
// the working directory of whatever launched the process. That is right for
// `dotnet run` (which sets it to the project folder) and for a service whose
// unit file sets WorkingDirectory, and WRONG for everything else — a systemd
// unit without it, a scheduled task, or an operator running
// `dotnet /opt/motiva/AuthWithAdmin.Server.dll` from their home directory.
//
// This is not theoretical: launching the built binary from an unrelated folder
// creates FinalProjectDB.db and App_Data/ IN THAT FOLDER, the migrator builds
// the schema and seeds demo data, and the application starts up looking healthy
// with none of the real data. Nothing errors. Resolving the database path from
// ContentRootPath does not by itself fix that, because ContentRootPath IS the
// working directory by default — so it is pinned here, once, before anything
// reads it.
//
// The rule, in order:
//   1. ASPNETCORE_CONTENTROOT / --contentRoot, if the operator set it. Always
//      wins; this is the supported host-level override.
//   2. The working directory, IF it contains appsettings.json. That file sits
//      next to the app in BOTH real layouts — the Server project folder during
//      development, and the publish output in production — so its presence is
//      what distinguishes "launched from the app" from "launched from
//      somewhere else". Development behaviour is therefore unchanged.
//   3. Otherwise AppContext.BaseDirectory: the folder the assemblies were
//      loaded from, which is the deployed application directory by definition
//      and cannot be influenced by the caller's shell.
var explicitContentRoot = Environment.GetEnvironmentVariable("ASPNETCORE_CONTENTROOT");
var contentRoot =
    !string.IsNullOrWhiteSpace(explicitContentRoot)
        ? explicitContentRoot
        : File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"))
            ? Directory.GetCurrentDirectory()
            : AppContext.BaseDirectory;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args            = args,
    ContentRootPath = contentRoot,
});

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port) && !builder.Environment.IsDevelopment())
{
    builder.WebHost.UseUrls($"http://*:{port}");
}
StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);

// ── SQLite: pin the database to the content root, not the working directory ──
//
// The connection string ships a RELATIVE Data Source, which SQLite resolves
// against the process's current working directory. See
// SqliteConnectionStringResolver for why that silently creates an empty
// database in production rather than failing.
//
// The resolved value is written BACK into configuration rather than handed to
// each consumer, and that is deliberate: DbRepository and DatabaseMigrator both
// read ConnectionStrings:DefaultConnection out of IConfiguration, so there is
// exactly ONE value and they cannot drift apart. Neither class changes.
//
// This runs before AddScoped<DbRepository>() and before the migrator, so every
// reader sees the absolute path.
builder.Configuration[SqliteConnectionStringResolver.ConfigurationKey] =
    SqliteConnectionStringResolver.Resolve(
        builder.Configuration.GetConnectionString(SqliteConnectionStringResolver.ConnectionName),
        builder.Environment.ContentRootPath);

// PRODUCTION ONLY: refuse to start on a missing database rather than let SQLite
// create an empty one. Placed HERE — immediately after the path is resolved and
// before any service registration, DbRepository or the migrator — because this
// is the last moment at which nothing has had the chance to open, and therefore
// create, the file. Reads directory metadata only; creates nothing. No-op
// outside Production, so development is unchanged.
SqliteConnectionStringResolver.EnsureDatabaseExistsInProduction(
    builder.Configuration.GetConnectionString(SqliteConnectionStringResolver.ConnectionName)!,
    builder.Environment.IsProduction());

// ── Reverse-proxy forwarded headers ─────────────────────────────────────────
//
// Behind a TLS-terminating proxy the app sees plain HTTP on an internal
// address, so Request.Scheme is "http" and Request.Host is the internal name.
// Four places compose absolute URLs from Scheme + Host + PathBase
// (GoogleCalendarController, TeamRegistrationController, AdminController,
// UsersController), and UseHttpsRedirection reads Scheme as well — so without
// this the Google OAuth redirect_uri is built as http:// and Google answers
// redirect_uri_mismatch.
//
// TRUST IS NOT GRANTED BLINDLY. With nothing configured the framework default
// applies: only loopback is trusted, which is correct for IIS/ANCM and for a
// proxy on the same host, and is a no-op when there is no proxy at all. A proxy
// on another address is trusted ONLY when the operator names it in
// ForwardedHeaders:KnownProxies / :KnownNetworks — see appsettings.Production
// .template.json and docs/DEPLOYMENT.md. Nothing is inferred from the request.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedProto
                             | ForwardedHeaders.XForwardedHost;

    var section       = builder.Configuration.GetSection("ForwardedHeaders");
    var knownProxies  = section.GetSection("KnownProxies").Get<string[]>()  ?? Array.Empty<string>();
    var knownNetworks = section.GetSection("KnownNetworks").Get<string[]>() ?? Array.Empty<string>();

    // Number of proxies between the client and this app. Raise it only when
    // there genuinely are that many, or a client-supplied header could be read
    // as a proxy's.
    var forwardLimit = section.GetValue<int?>("ForwardLimit");
    if (forwardLimit is > 0) options.ForwardLimit = forwardLimit;

    // The defaults are REPLACED only when the operator has named something.
    // Leaving them in place alongside an explicit list would widen trust, not
    // narrow it.
    if (knownProxies.Length > 0 || knownNetworks.Length > 0)
    {
        options.KnownProxies.Clear();
        // KnownIPNetworks, not the obsolete KnownNetworks: same list, typed to
        // System.Net.IPNetwork.
        options.KnownIPNetworks.Clear();

        foreach (var proxy in knownProxies)
            if (IPAddress.TryParse(proxy, out var address))
                options.KnownProxies.Add(address);

        // An unparseable entry is skipped rather than throwing: a typo in
        // configuration must not take the application down at startup, and
        // skipping one can only NARROW trust, never widen it.
        foreach (var network in knownNetworks)
            if (System.Net.IPNetwork.TryParse(network, out var parsed))
                options.KnownIPNetworks.Add(parsed);
    }
});

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

// FIRST IN THE PIPELINE, and that placement is the whole point: this rewrites
// Request.Scheme / Host / RemoteIpAddress from the X-Forwarded-* headers, so it
// has to run before anything that reads them — UseHsts and UseHttpsRedirection
// immediately below, the static-file and routing middleware, and every
// controller that builds an absolute URL from Scheme + Host + PathBase.
//
// Safe to call unconditionally: with no trusted proxy configured (the default,
// and the local-development case) there is nothing to apply and this is a
// no-op. See the Configure<ForwardedHeadersOptions> block above for the trust
// model.
app.UseForwardedHeaders();

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

