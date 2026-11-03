using System.Threading.RateLimiting;
using Auth.Infrastructure;
using FluentValidation;
using FormEngine.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi;
using NWFM.Platform.Api.TenantSeeds;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Behaviors;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Http;
using NWFM.Shared.MultiTenancy;
using NWFM.Shared.Options;
using Platform.Api;
using Platform.Api.Middleware;
using Platform.Api.Services;
using Platform.Application.Common.Behaviors;
using Platform.Infrastructure;
using Tasks.Infrastructure;
using Tenancy.Infrastructure;
using Workflow.Infrastructure;

// The control plane. Never binds a request to a tenant: work against one tenant (provisioning,
// migrations, tenant administrators, support) opens that tenant's scope explicitly, with the owner
// credential. It never mints tenant tokens either: a support session hands the tenant app a
// one-time code instead.
const string CorsPolicyName = "PlatformWeb";

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

builder.AddTenancyRuntime(TenantConnectionRole.Administration);

// Tenant module databases and services, reached only inside an explicit tenant scope. No module HTTP
// surface, no tenant sign-in and no tenant workers are registered here.
builder.AddAuthServices();
builder.Services.AddWorkflowInfrastructure(builder.Configuration);
builder.AddFormEngineInfrastructure();
builder.AddTasksInfrastructure();
builder.Services.AddSingleton<ITenantDatabaseModule, WorkflowDemoSeed>();
builder.AddPlatformInfrastructure();

builder.Services.AddOptions<DatabaseStartupOptions>().BindConfiguration(DatabaseStartupOptions.SectionName);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, NoTenantUser>();
builder.Services.AddScoped<IWorkflowActorContext, SystemWorkflowActor>();

// Replaces the tenancy runtime's anonymous default: here the platform bearer scheme is real.
builder.Services.Replace(ServiceDescriptor.Scoped<IPlatformUser, CurrentPlatformUser>());

builder.Services.AddMediatR(c =>
{
    c.RegisterServicesFromAssembly(typeof(Platform.Application.AssemblyMarker).Assembly);
    // Seeds publish forms and workflows through the modules' own use cases.
    c.RegisterServicesFromAssembly(typeof(Workflow.Application.AssemblyMarker).Assembly);
    c.RegisterServicesFromAssembly(typeof(FormEngine.Application.AssemblyMarker).Assembly);
    c.RegisterServicesFromAssembly(typeof(Tasks.Application.AssemblyMarker).Assembly);
});
builder.Services.AddValidatorsFromAssembly(typeof(Platform.Application.AssemblyMarker).Assembly);
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PlatformAuthorizationBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(PlatformRateLimits.Public, http => FixedWindow(http, 60));
    options.AddPolicy(PlatformRateLimits.DatabaseTest, http => FixedWindow(http, 6));
    options.AddPolicy(PlatformRateLimits.Login, http => FixedWindow(http, 10));
});

// Only the platform web app (landing, signup, console) calls this API; tenant subdomains do not.
var corsOrigins = builder.Configuration.GetSection("CorsOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddPolicy(CorsPolicyName, p => p
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in builder.Configuration.GetSection("KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
});

builder.Services.AddControllers()
    .AddApplicationPart(typeof(PlatformRateLimits).Assembly)
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "NWFM Platform", Version = "v1" });
    c.CustomSchemaIds(type => type.FullName!.Replace('+', '.'));
    c.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Platform staff token"
    });
    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("bearer", document)] = new List<string>()
    });
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler(handler => handler.Run(async http =>
{
    var error = http.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var (status, code, message) = error switch
    {
        TenantAccessException tenant => (tenant.StatusCode, tenant.Code, tenant.Message),
        DomainException domain => (domain.StatusCode ?? 400, domain.Code ?? "Domain.RuleViolated", domain.Message),
        ValidationException validation => (400, "Validation.Failed", string.Join(" ", validation.Errors.Select(e => e.ErrorMessage))),
        UnauthorizedAccessException => (403, "Platform.Forbidden", "Access denied."),
        _ => (500, "Server.Error", "The request could not be completed. See the server log.")
    };
    http.Response.StatusCode = status;
    await http.Response.WriteAsJsonAsync(ResultActionResults.FailureBody(code, message));
}));

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors(CorsPolicyName);
app.UseRateLimiter();

// Catalog first (migrate, plans, configuration tenants, first platform admin), then every
// configuration tenant's database (and, where configured, every active tenant's). The tenant API
// never does either.
await app.InitialisePlatformAsync();

app.UseAuthentication();
app.UsePlatformScope();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", application = "NWFM.Platform" }));

app.Run();

static RateLimitPartition<string> FixedWindow(HttpContext http, int permitsPerMinute) =>
    RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = permitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

/// <summary>Seeds and provisioning act as the system, never as a person.</summary>
internal sealed class SystemWorkflowActor : IWorkflowActorContext
{
    public Guid ActorId => Guid.Empty;
    public Guid ParticipantId => Guid.Empty;
}
