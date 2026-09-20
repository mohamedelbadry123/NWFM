using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using NWFM.Api.Services;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Behaviors;
using NWFM.Shared.Integration.Workflow;
using Auth.Infrastructure;
using Workflow.Infrastructure;
using Workflow.Infrastructure.Persistence;
using AppContext = NWFM.Api.Services.ApplicationContext;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;

builder.Services.AddOptions<ApplicationOptions>().BindConfiguration("Application")
    .Validate(o => o.TenantId != Guid.Empty, "Tenant ID is required.").ValidateOnStart();

builder.Services.AddScoped<AppContext>();
builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<AppContext>());
builder.Services.AddScoped<IWorkflowActorContext>(sp => sp.GetRequiredService<AppContext>());
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.AddAuthInfrastructure(connectionString);
builder.Services.AddWorkflowInfrastructure(connectionString, builder.Configuration);

builder.Services.AddMediatR(c =>
{
    c.RegisterServicesFromAssembly(typeof(Workflow.Application.AssemblyMarker).Assembly);
    c.RegisterServicesFromAssembly(typeof(Auth.Application.AssemblyMarker).Assembly);
});
builder.Services.AddValidatorsFromAssembly(typeof(Workflow.Application.AssemblyMarker).Assembly);
builder.Services.AddValidatorsFromAssembly(typeof(Auth.Application.AssemblyMarker).Assembly);
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

builder.Services.AddScoped<IWorkflowOutcomeHandler, StandaloneOutcomeHandler>();
builder.Services.AddScoped<TenantScopeFilter>();

var corsOrigins = builder.Configuration.GetSection("CorsOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(corsOrigins).AllowAnyMethod().AllowAnyHeader().AllowCredentials()));

builder.Services.AddControllers(o => { o.Filters.AddService<TenantScopeFilter>(); o.Filters.Add<FailureResultFilter>(); })
    .AddApplicationPart(typeof(Workflow.Api.Controllers.WorkItemsController).Assembly)
    .AddApplicationPart(typeof(Auth.Api.Controllers.AuthController).Assembly)
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

app.UseExceptionHandler(handler => handler.Run(async http =>
{
    var error = http.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    if (error is ValidationException validation)
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
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<RequestContextMiddleware>();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok", application = "NWFM" }));

// Auth database: migrate, SQL objects, seed — controlled by DatabaseStartup flags.
await app.InitialiseAuthDatabaseAsync();

// Workflow database: always migrates (existing behavior).
await using (var scope = app.Services.CreateAsyncScope())
{
    await DatabaseInitializer.InitializeAsync(scope.ServiceProvider);
    await WorkflowWorkspaceDemo.InitializeAsync(scope.ServiceProvider, app.Configuration);
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
