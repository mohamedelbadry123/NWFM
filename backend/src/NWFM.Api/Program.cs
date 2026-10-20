using Auth.Infrastructure;
using FluentValidation;
using FormEngine.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using NWFM.Api.Hosting;
using NWFM.Api.Services;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Behaviors;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Options;
using Tasks.Infrastructure;
using Workflow.Infrastructure;
using AppContext = NWFM.Api.Services.ApplicationContext;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
var databaseStartup = builder.Configuration.GetSection(DatabaseStartupOptions.SectionName)
    .Get<DatabaseStartupOptions>() ?? new();
TenantDatabaseBoundary.ValidateStartup(databaseStartup);
await TenantDatabaseBoundary.VerifyAsync(connectionString,
    builder.Configuration.GetValue<Guid>("Application:TenantId"), databaseStartup.InitializeOnly);

builder.Services.AddOptions<ApplicationOptions>().BindConfiguration("Application")
    .Validate(o => o.TenantId != Guid.Empty, "Tenant ID is required.").ValidateOnStart();

builder.Services.AddScoped<AppContext>();
builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<AppContext>());
builder.Services.AddScoped<IWorkflowActorContext>(sp => sp.GetRequiredService<AppContext>());
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.AddAuthInfrastructure(connectionString);
builder.Services.AddWorkflowInfrastructure(connectionString, builder.Configuration);
builder.AddFormEngineInfrastructure(connectionString);
builder.AddTasksInfrastructure(connectionString);

// Media uploads are larger than Kestrel's default body limit allows.
builder.AddRequestBodyLimits();

builder.Services.AddMediatR(c =>
{
    c.RegisterServicesFromAssembly(typeof(Workflow.Application.AssemblyMarker).Assembly);
    c.RegisterServicesFromAssembly(typeof(Auth.Application.AssemblyMarker).Assembly);
    c.RegisterServicesFromAssembly(typeof(FormEngine.Application.AssemblyMarker).Assembly);
    c.RegisterServicesFromAssembly(typeof(Tasks.Application.AssemblyMarker).Assembly);
});
builder.Services.AddValidatorsFromAssembly(typeof(Workflow.Application.AssemblyMarker).Assembly);
builder.Services.AddValidatorsFromAssembly(typeof(Auth.Application.AssemblyMarker).Assembly);
builder.Services.AddValidatorsFromAssembly(typeof(FormEngine.Application.AssemblyMarker).Assembly);
builder.Services.AddValidatorsFromAssembly(typeof(Tasks.Application.AssemblyMarker).Assembly);
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

builder.Services.AddScoped<IWorkflowOutcomeHandler, StandaloneOutcomeHandler>();
builder.Services.AddScoped<TenantScopeFilter>();
builder.Services.AddOptions<TenantResourceOptions>().BindConfiguration("TenantResourceLimits")
    .Validate(o => o.RequestsPerMinute is > 0 and <= 100000
        && o.PrincipalRequestsPerMinute is > 0 and <= 10000
        && o.LoginRequestsPerMinute is > 0 and <= 100
        && o.ConcurrentRequests is > 0 and <= 128, "Tenant resource limits must be bounded and positive.")
    .ValidateOnStart();
builder.Services.AddSingleton<TenantResourceLimits>();
builder.Services.AddSingleton<TenantSecuritySignals>();

var corsOrigins = builder.Configuration.GetSection("CorsOrigins").Get<string[]>() ?? [];
// Content-Disposition is exposed so a cross-origin client can read a report's file name.
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(corsOrigins).AllowAnyMethod().AllowAnyHeader().AllowCredentials().WithExposedHeaders("Content-Disposition")));

builder.Services.AddControllers(o => { o.Filters.AddService<TenantScopeFilter>(); o.Filters.Add<FailureResultFilter>(); })
    .AddApplicationPart(typeof(Workflow.Api.Controllers.WorkItemsController).Assembly)
    .AddApplicationPart(typeof(Auth.Api.Controllers.AuthController).Assembly)
    .AddApplicationPart(typeof(FormEngine.Api.Controllers.FormsController).Assembly)
    .AddApplicationPart(typeof(Tasks.Api.Controllers.TasksController).Assembly)
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.CustomSchemaIds(OpenApiSchemaId);

    c.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token"
    });
    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("bearer", document)] = new List<string>()
    });

    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Name = "X-API-KEY",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "API key for external consumers"
    });
});

builder.Services.AddProblemDetails();

var app = builder.Build();
// Bind and verify storage before either bootstrap work or the request host can use it.
_ = app.Services.GetRequiredService<NWFM.Shared.Storage.IFileStorage>();

app.UseExceptionHandler(handler => handler.Run(async http =>
{
    var error = http.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    if (error is NWFM.Shared.Exceptions.WorkflowQueueCapacityException)
    {
        http.Response.StatusCode = 429;
        http.Response.Headers.RetryAfter = "60";
        await http.Response.WriteAsJsonAsync(new { code = "Tenant.QueueCapacity", message = "Queued work capacity is exhausted. Try again later." });
    }
    else if (error is ValidationException validation)
    {
        http.Response.StatusCode = 400;
        await http.Response.WriteAsJsonAsync(new { code = "Validation.Failed", message = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)) });
    }
    else if (error is UnauthorizedAccessException)
    {
        http.Response.StatusCode = 403;
        await http.Response.WriteAsJsonAsync(new { code = "Auth.Forbidden", message = error.Message ?? "Access denied." });
    }
    else
    {
        http.Response.StatusCode = 500;
        await http.Response.WriteAsJsonAsync(new { code = "Server.Error", message = "The request could not be completed. See the server log." });
    }
}));

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseMiddleware<TenantRequestBudgetMiddleware>();
app.UseAuthentication();
app.UseMiddleware<TenantPrincipalBudgetMiddleware>();
app.UseMiddleware<RequestContextMiddleware>();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok", application = "NWFM" }));

if (databaseStartup.InitializeOnly)
{
    // Auth database: migrate, SQL objects, seed — controlled by DatabaseStartup flags.
    await app.InitialiseAuthDatabaseAsync();

    // FormEngine database: migrate, ensure each published form's submission table, seed the example forms.
    await app.InitialiseFormEngineDatabaseAsync();

    // Tasks database: migrate, seed task types bound to the forms seeded above.
    await app.InitialiseTasksDatabaseAsync();

    // Workflow database: migration and seed operations respect the same initialization flags.
    await using (var scope = app.Services.CreateAsyncScope())
    {
        await DatabaseInitializer.InitializeAsync(scope.ServiceProvider);
        await WorkflowWorkspaceDemo.InitializeAsync(scope.ServiceProvider, app.Configuration);
    }
    return;
}

app.Run();

static string OpenApiSchemaId(Type type)
{
    if (!type.IsGenericType)
        return type.FullName!.Replace('+', '.');

    var name = $"{type.Namespace}.{type.Name.Split('`')[0]}".Replace('+', '.');
    return name + "Of" + string.Join("_", type.GetGenericArguments().Select(OpenApiSchemaId));
}

public partial class Program { }
