using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.Google;
using System.Text;
using Microsoft.AspNetCore.Authentication.OAuth;

var builder = WebApplication.CreateBuilder(args);

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

//Mail
builder.Services.AddSingleton<EmailHelper>();


//JWT
var jwtSettings = builder.Configuration.GetSection("JWTSettings");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme; // Only set one challenge scheme!
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
app.MapStaticAssets();
app.MapFallbackToFile("index.html");

await DatabaseMigrator.MigrateAsync(app.Configuration);

app.Run();