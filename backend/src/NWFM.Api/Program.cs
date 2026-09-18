using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Api.Services;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Behaviors;
using NWFM.Shared.Integration.Workflow;
using Workflow.Infrastructure;
using Workflow.Infrastructure.Persistence;
using AppContext = NWFM.Api.Services.ApplicationContext;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptions<ApplicationOptions>().BindConfiguration("Application")
    .Validate(o => o.TenantId != Guid.Empty && o.DefaultActorId != Guid.Empty, "Tenant and default actor IDs are required.").ValidateOnStart();
builder.Services.AddScoped<AppContext>();
builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<AppContext>());
builder.Services.AddScoped<IWorkflowActorContext>(sp => sp.GetRequiredService<AppContext>());
builder.Services.AddWorkflowInfrastructure(builder.Configuration.GetConnectionString("DefaultConnection")!, builder.Configuration);
builder.Services.AddMediatR(c => c.RegisterServicesFromAssembly(typeof(Workflow.Application.AssemblyMarker).Assembly));
builder.Services.AddValidatorsFromAssembly(typeof(Workflow.Application.AssemblyMarker).Assembly);
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
builder.Services.AddScoped<IWorkflowOutcomeHandler, StandaloneOutcomeHandler>();
builder.Services.AddScoped<TenantScopeFilter>();
builder.Services.AddControllers(o => o.Filters.AddService<TenantScopeFilter>())
    .AddApplicationPart(typeof(Workflow.Api.Controllers.WorkItemsController).Assembly)
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.CustomSchemaIds(t => !t.IsGenericType ? t.FullName!.Replace('+', '.') :
    t.Namespace + "." + t.Name.Split('`')[0] + "Of" + string.Join("_", t.GetGenericArguments().Select(a => a.Name.Split('`')[0]))));
builder.Services.AddProblemDetails();
var app = builder.Build();
app.UseExceptionHandler(handler => handler.Run(async http => {
    var error = http.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    if (error is ValidationException validation) {
        http.Response.StatusCode = 400;
        await http.Response.WriteAsJsonAsync(new { code = "Validation.Failed", message = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)) });
    } else {
        http.Response.StatusCode = 500;
        await http.Response.WriteAsJsonAsync(new { code = "Server.Error", message = "The request could not be completed. See the server log." });
    }
}));
app.UseSwagger();
app.UseSwaggerUI();
app.UseMiddleware<RequestContextMiddleware>();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", application = "NWFM" }));
if (app.Configuration.GetValue<bool>("Application:InitializeDatabase")) {
    await using var scope = app.Services.CreateAsyncScope();
    await DatabaseInitializer.InitializeAsync(scope.ServiceProvider);
}
app.Run();
public partial class Program { }
