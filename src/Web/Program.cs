using CompanyAccessManagement.Infrastructure.Data;
using CompanyAccessManagement.Infrastructure.Identity;
using CompanyAccessManagement.Web.Middleware;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddServiceDefaults();

builder.AddKeyVaultIfConfigured();
builder.AddApplicationServices();
builder.AddInfrastructureServices();
builder.AddWebServices();

var app = builder.Build();

// ProblemDetailsExceptionHandler maps NotFoundException to 404, which must not be treated as a misconfigured handler.
app.UseExceptionHandler(new ExceptionHandlerOptions { AllowStatusCode404Response = true });

// Configure the HTTP request pipeline.
// For TestIntegration and Development, initialize database
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("TestIntegration"))
{
    await app.InitialiseDatabaseAsync();
}
else
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// CORS is enabled only for explicitly configured origins (appsettings.Development.json locally).
var allowedOrigins = app.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

if (allowedOrigins.Length > 0)
{
    app.UseCors(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials());
}

app.UseFileServer();

app.UseAuthentication();
app.UseMiddleware<WorkspaceContextMiddleware>();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference();

app.Map("/", () => Results.Redirect("/scalar"));

app.MapDefaultEndpoints();
app.MapIdentityApi<ApplicationUser>();
app.MapEndpoints(typeof(Program).Assembly);

app.Run();