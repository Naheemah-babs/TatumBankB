
using Dapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using TatumWelcome2.BLL;
using TatumWelcome2.Database;
using TatumWelcome2.Interfaces;
using TatumWelcome2.Repositories;
using TatumWelcome2.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("https://localhost:5001");

builder.Services.AddControllers();

string jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("JWT signing key is missing.");

string jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("JWT issuer is missing.");

string jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("JWT audience is missing.");

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
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode =
        StatusCodes.Status429TooManyRequests;

    options.AddPolicy("login", context =>
    {
        string ip = context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            ip,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    options.AddPolicy("pin-transactions", context =>
    {
        string? accountNumber = context.User.Claims
            .FirstOrDefault(claim =>
                claim.Type == ClaimTypes.NameIdentifier ||
                claim.Type == "sub")?.Value;

        string partitionKey = accountNumber
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token."
    });

    options.AddSecurityDefinition("AdminKey", new OpenApiSecurityScheme
    {
        Name = "X-Admin-Key",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Enter your admin API key."
    });
});

builder.Services.AddSingleton<DbConnectionFactory>();
builder.Services.AddSingleton<IAccountRepository, AccountRepository>();

builder.Services.AddHttpClient<IBankApiClient, BankApiClient>();

builder.Services.AddSingleton<InterbankTransferService>();
builder.Services.AddSingleton<InterbankOutgoingTransferService>();
builder.Services.AddSingleton<AdminService>();

builder.Services.AddSingleton<OutgoingInterbankTransferRepository>();
builder.Services.AddSingleton<IncomingInterbankTransferRepository>();

builder.Services.AddSingleton<BillPaymentRepository>();

builder.Services.AddSingleton<BillPurchaseService>();
builder.Services.AddSingleton<AirtimeService>();
builder.Services.AddSingleton<DataService>();

builder.Services.AddHostedService<BillPaymentReconciliationService>();

builder.Services.AddHostedService<InterbankReconciliationService>();

builder.Logging.AddFilter(
    "System.Net.Http.HttpClient", LogLevel.Warning);

builder.Logging.AddFilter(
    "Microsoft.AspNetCore", LogLevel.Warning);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

try
{
    var factory =
        app.Services.GetRequiredService<DbConnectionFactory>();

    using var connection = factory.CreateConnection();
    connection.Open();

    int result = connection.ExecuteScalar<int>("SELECT 1");

    if (result == 1)
        app.Logger.LogInformation("Bank B MySQL connection successful!");
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Bank B MySQL connection error.");
}

app.MapControllers();

app.Run();
