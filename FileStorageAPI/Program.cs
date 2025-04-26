using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Minio;
using Serilog;
using FileStorageAPI.HealthCheck;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
builder.Host.UseSerilog((context, configuration) =>
                configuration.ReadFrom.Configuration(context.Configuration).Enrich.WithProperty("Application", context.Configuration.GetValue<string>("Serilog:Properties:Application")));

// Register MinIO client as IMinioClient using a factory
builder.Services.AddSingleton<IMinioClient>(sp =>
{
    var config = builder.Configuration.GetSection("Minio");
    return new MinioClient()
        .WithEndpoint(config["Endpoint"])
        .WithCredentials(config["AccessKey"], config["SecretKey"])
        .WithSSL(bool.Parse(config["SSL"]))
        .Build();
});

// Register MinIO health check
builder.Services.AddHealthChecks()
    .AddCheck<MinioHealthCheck>("MinIO_Health");

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "FileStorageAPI", Version = "v1" });

    c.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml"));

    c.MapType<IFormFile>(() => new Microsoft.OpenApi.Models.OpenApiSchema
    {
        Type = "string",
        Format = "binary"
    });
});

// Configure JWT authentication
#region Authentication
builder.Services.AddAuthentication("Bearer")
   .AddJwtBearer("Bearer", options =>
   {
       // IdentityModelEventSource.ShowPII = true;
       options.Authority = builder.Configuration.GetSection("DerayahIdentityServer:AuthorityServer").Value;
       options.RequireHttpsMetadata = false; // Should use HTTPS on Production
       options.TokenValidationParameters = new TokenValidationParameters
       {
           ValidateAudience = false,
           ValidateIssuer = true,
           ValidateLifetime = true,
           ValidIssuers = builder.Configuration.GetSection("DerayahIdentityServer:AllowedIssuers")?.GetChildren()?.Select(x => x.Value)?.ToList(),
           ValidateIssuerSigningKey = false
       };
   });
#endregion
#region Authorization
//  adds an authorization policy to make sure the token is for scope 'MoyasarAPI'
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("MoyasarAPI", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("scope", builder.Configuration.GetSection("DerayahIdentityServer:AllowedScopes")?.GetChildren()?.Select(x => x.Value)?.ToList());
    });
});
#endregion

var app = builder.Build();

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// Map health check endpoint
app.MapHealthChecks("/health");

app.MapGet("/", async context =>
{
    await context.Response.WriteAsync("Welcome to FileStorageAPI!");
});

app.MapControllers();

app.Run();
