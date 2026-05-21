using System.Text;
using System.Threading.RateLimiting;
using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using TestNativeMobileBackendApi.Configuration;
using TestNativeMobileBackendApi.Hosting;
using TestNativeMobileBackendApi.Hubs;
using TestNativeMobileBackendApi.Interfaces;
using TestNativeMobileBackendApi.Models;
using TestNativeMobileBackendApi.Services;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    var dockerOptions = builder.Configuration.GetSection(DockerOptions.SectionName).Get<DockerOptions>()
        ?? new DockerOptions();

    if (dockerOptions.Enabled)
    {
        await DockerPostgresBootstrap.EnsureRunningAsync(
            builder.Environment.ContentRootPath,
            dockerOptions);
    }
}

builder.AddNpgsqlDataSource("db", configureDataSourceBuilder: dataSourceBuilder =>
{
    if (string.IsNullOrEmpty(dataSourceBuilder.ConnectionStringBuilder.Password))
    {
        dataSourceBuilder.UsePeriodicPasswordProvider(async (_, cancellationToken) =>
        {
            var credentials = new DefaultAzureCredential();
            var token = await credentials.GetTokenAsync(
                new TokenRequestContext(["https://ossrdbms-aad.database.windows.net/.default"]),
                cancellationToken);
            return token.Token;
        }, TimeSpan.FromHours(24), TimeSpan.FromSeconds(10));
    }
});

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration is required.");
var envSigningKey = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY");
if (!string.IsNullOrWhiteSpace(envSigningKey))
{
    jwtOptions.SigningKey = envSigningKey;
}

if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey) || jwtOptions.SigningKey.Length < 32)
{
    throw new InvalidOperationException("JWT signing key must be configured and at least 32 characters.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/chatHub"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.AdminOnly, policy => policy.RequireRole(AppRoles.Admin));
    options.AddPolicy(AuthorizationPolicies.ChatUser, policy => policy.RequireRole(AppRoles.User, AppRoles.Admin));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var permitLimit = builder.Environment.IsEnvironment("Testing") ? 100 : 5;
    options.AddPolicy("auth-login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            }));
});

builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.AddSingleton<PasswordHasherService>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddControllers();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/chatHub");
app.MapRazorPages();

app.Run();

public partial class Program;
