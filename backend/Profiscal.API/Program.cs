using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Profiscal.API.Extensions;
using Profiscal.API.Middleware;
using Profiscal.API.Services;
using Profiscal.Application;
using Profiscal.Application.Common.Interfaces;
using Profiscal.Domain.Entities;
using Profiscal.Infrastructure;
using FiscalPlatform.Infrastructure;
using FiscalPlatform.Application.Common.Behaviours;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Domain.Repositories;
using FluentValidation;
using MediatR;
using Profiscal.API.Fiscal;

// Load .env before the host is built so env vars are visible to the config system.
// Search upward from the current directory so a single .env at the repo root works
// regardless of where `dotnet run` is launched from (and is shared with embed_server.py).
// .env is in .gitignore — never committed.
static string? FindDotEnv(string start)
{
    for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, ".env");
        if (File.Exists(candidate)) return candidate;
    }
    return null;
}
var dotEnvPath = FindDotEnv(Directory.GetCurrentDirectory());
if (dotEnvPath is not null)
{
    DotNetEnv.Env.Load(dotEnvPath);
    Console.WriteLine($"[env] loaded {dotEnvPath}");
}
else
{
    Console.WriteLine("[env] no .env found — using appsettings / environment only");
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerWithJwt();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ── Fiscal engine (colleague's GraphRAG pipeline) ─────────────────────────────
builder.Services.AddHttpClient();
builder.Services.AddFiscalEngine();
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<
        FiscalPlatform.Application.Consultation.Commands
        .GenerateConsultation.GenerateConsultationCommand>();
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehaviour<,>));
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
});
builder.Services.AddValidatorsFromAssemblyContaining<
    FiscalPlatform.Application.Consultation.Commands
    .GenerateConsultation.GenerateConsultationCommandValidator>();

// Chat agent resolved directly (for the SSE streaming endpoint, alongside MediatR).
builder.Services.AddScoped<
    FiscalPlatform.Application.Chat.Queries.Chat.ChatQueryHandler>();

// Legal search engine: Elasticsearch BM25 (restored — better ranking than the
// Neo4j substring search, and what the instructor requires). Needs an ES instance
// with the `tunisian_legal` index. Neo4jSearchAgent stays in the codebase as a
// no-ES fallback (just swap this line back if ES is unavailable).
builder.Services.AddSingleton<ISearchAgent,
    FiscalPlatform.Infrastructure.Search.ElasticsearchSearchAgent>();

// EF replacements for consultations/ratings persistence.
builder.Services.AddScoped<IConsultationRepository, EfConsultationRepository>();
builder.Services.AddScoped<IFeedbackAgent, EfFeedbackAgent>();
builder.Services.AddScoped<ConsultationStore>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRequestContext, HttpRequestContext>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = builder.Configuration["Jwt:Issuer"],
            ValidAudience            = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(
                    Environment.GetEnvironmentVariable("JWT__KEY")
                    ?? builder.Configuration["Jwt:Key"]
                    ?? throw new InvalidOperationException("Jwt:Key / JWT__KEY is not configured."))),
            ClockSkew                = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddCors(options =>
    options.AddPolicy("AllowFrontend", policy =>
        policy.WithOrigins(builder.Configuration["AllowedOrigins"] ?? "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()));

var app = builder.Build();

// Apply pending migrations, then seed roles + the default admin account (idempotent).
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider
        .GetRequiredService<Profiscal.Infrastructure.Persistence.AppDbContext>()
        .Database.MigrateAsync();

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    foreach (var role in new[] { "Admin", "User" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole<Guid>(role));
    }

    var adminEmail    = app.Configuration["Seed:AdminEmail"];
    var adminPassword = app.Configuration["Seed:AdminPassword"];
    if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        if (await userManager.FindByEmailAsync(adminEmail) is null)
        {
            var admin = new AppUser
            {
                FirstName = app.Configuration["Seed:AdminFirstName"] ?? "Taxmind",
                LastName  = app.Configuration["Seed:AdminLastName"] ?? "Admin",
                Email     = adminEmail,
                UserName  = adminEmail,
                EmailConfirmed = true
            };
            var created = await userManager.CreateAsync(admin, adminPassword);
            if (created.Succeeded)
                await userManager.AddToRoleAsync(admin, "Admin");
            else
                app.Logger.LogWarning("Admin seeding failed: {Errors}",
                    string.Join(", ", created.Errors.Select(e => e.Description)));
        }
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseCors("AllowFrontend");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
