using CompanyAccessManagement.Infrastructure.Data;
using CompanyAccessManagement.Infrastructure.Identity;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddServiceDefaults();

builder.AddKeyVaultIfConfigured();
builder.AddApplicationServices();

// Add infrastructure services for all environments except "Testing" (unit tests use their own setup)
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.AddInfrastructureServices();
}

builder.AddWebServices();

var app = builder.Build();

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

// Configure CORS - allow specific origins from configuration, or allow any in development
var allowedOrigins = app.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() 
    ?? (app.Environment.IsDevelopment() ? new[] { "http://localhost:4200", "http://localhost:3000", "http://localhost:5000" } : Array.Empty<string>());

if (allowedOrigins.Length > 0)
{
    app.UseCors(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials());
}
else
{
    // In development without explicit config, allow common dev origins
    app.UseCors(policy => policy
        .AllowAnyMethod()
        .AllowAnyHeader()
        .SetIsOriginAllowed(_ => true)
        .AllowCredentials());
}

app.UseFileServer();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseExceptionHandler(options => { });

#if (UseApiOnly)
app.Map("/", () => Results.Redirect("/scalar"));
#endif

app.MapDefaultEndpoints();
app.MapIdentityApi<ApplicationUser>();
app.MapEndpoints(typeof(Program).Assembly);

#if (!UseApiOnly)
app.MapFallbackToFile("index.html");
#endif

app.Run();