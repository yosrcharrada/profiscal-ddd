using FiscalPlatform.API.Middleware;
using FiscalPlatform.Application.Common.Behaviours;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Infrastructure;
using FiscalPlatform.Infrastructure.Agents;
using FiscalPlatform.Infrastructure.Search;
using MediatR;
using FluentValidation;
using DotNetEnv;

// ── Load .env ─────────────────────────────────────────────────────────────────
var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (!File.Exists(envPath))
    envPath = Path.Combine(Directory.GetCurrentDirectory(), "..", ".env");
Env.Load(envPath);

var builder = WebApplication.CreateBuilder(args);

// ── Listen on port 8080 (same as old project) ─────────────────────────────────
builder.WebHost.UseUrls("http://localhost:8080");

// ── MVC ───────────────────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

// ── MediatR (CQRS) — discovers all Commands/Queries in Application layer ──────
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<
        FiscalPlatform.Application.Consultation.Commands
        .GenerateConsultation.GenerateConsultationCommand>();
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehaviour<,>));
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
});

// ── FluentValidation ──────────────────────────────────────────────────────────
builder.Services.AddValidatorsFromAssemblyContaining<
    FiscalPlatform.Application.Consultation.Commands
    .GenerateConsultation.GenerateConsultationCommandValidator>();

// ── Infrastructure layer (agents, repositories, domain services) ───────────────
builder.Services.AddInfrastructure();

// ── Agents that need IWebHostEnvironment (must be registered in API layer) ─────
builder.Services.AddSingleton<IRetrievalAgent,          RetrievalAgent>();
builder.Services.AddSingleton<IDocumentGenerationAgent, DocumentGenerationAgent>();
builder.Services.AddSingleton<ISearchAgent,             ElasticsearchSearchAgent>();

// ── Swagger (API documentation) ───────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "ProFiscal API", Version = "v1",
        Description = "Plateforme de Consultation Fiscale Tunisienne — EY Tunisia" });
});

// ── CORS ──────────────────────────────────────────────────────────────────────
builder.Services.AddCors(o =>
    o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

// ── Middleware pipeline ────────────────────────────────────────────────────────
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseStaticFiles();
app.UseRouting();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "ProFiscal v1"));
}

// ── Routes ────────────────────────────────────────────────────────────────────
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.MapControllers();

app.Run();
