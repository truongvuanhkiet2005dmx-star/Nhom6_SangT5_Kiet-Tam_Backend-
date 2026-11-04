using System.Text;
using System.Threading.RateLimiting;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SmartMoney.Api.Data;
using SmartMoney.Api.Models;
using SmartMoney.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddProblemDetails();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    string GetPartitionKey(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp == null || System.Net.IPAddress.IsLoopback(remoteIp))
            return "loopback";
        return remoteIp.ToString();
    }

    options.AddPolicy("auth", context =>
    {
        var key = GetPartitionKey(context);
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddPolicy("join", context =>
    {
        var key = GetPartitionKey(context);
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

allowedOrigins = allowedOrigins
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim())
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

if (builder.Environment.IsDevelopment() && allowedOrigins.Length == 0)
{
    allowedOrigins =
    [
        "http://localhost:3000",
        "http://localhost:5087",
        "https://localhost:7076"
    ];
}

if (!builder.Environment.IsDevelopment() && allowedOrigins.Length == 0)
{
    Console.Error.WriteLine(
        "Cảnh báo: Cors:AllowedOrigins chưa được cấu hình trong Production; Flutter Web bị chặn bởi CORS.");
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("FlutterWeb", policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.AllowedForNewUsers = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireDigit = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Thiếu cấu hình Jwt:Key.");
var jwtKeyBytes = Encoding.UTF8.GetBytes(jwtKey);
if (jwtKeyBytes.Length < 32)
    throw new InvalidOperationException(
        "Jwt:Key phải có ít nhất 32 byte khi mã hóa UTF-8 để dùng HS256.");

var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = "Phiên đăng nhập không hợp lệ hoặc đã hết hạn",
                    Detail = "Vui lòng đăng nhập lại để nhận access token mới."
                });
            }
        };
    });

builder.Services.AddAuthorization();

FirebaseMessaging? firebaseMessaging = null;
FirebaseApp? firebaseApp = null;
var serviceAccountPath =
    builder.Configuration["Firebase:ServiceAccountPath"];

if (string.IsNullOrWhiteSpace(serviceAccountPath))
{
    Console.Error.WriteLine(
        "Firebase Admin chưa được cấu hình: thiếu Firebase:ServiceAccountPath.");
}
else if (!File.Exists(serviceAccountPath))
{
    Console.Error.WriteLine(
        "Firebase Admin chưa được cấu hình: service account file không tồn tại.");
}
else
{
    try
    {
        firebaseApp = FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential.FromFile(serviceAccountPath),
            ProjectId = builder.Configuration["Firebase:ProjectId"]
        });

        firebaseMessaging = FirebaseMessaging.GetMessaging(firebaseApp);
        builder.Services.AddSingleton(FirebaseAuth.GetAuth(firebaseApp));
    }
    catch (Exception ex)
    {
        firebaseApp = null;
        firebaseMessaging = null;
        Console.Error.WriteLine(
            $"Firebase Admin không khởi tạo được: {ex.Message}");
    }
}

builder.Services.AddSingleton(
    new PushNotificationService(firebaseMessaging));
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<BudgetAlertService>();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SmartMoney API",
        Version = "v1"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Nhập token theo dạng: Bearer {token}"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var status = StatusCodes.Status500InternalServerError;
        var detail = app.Environment.IsDevelopment()
            ? feature?.Error.Message ?? "Lỗi máy chủ không xác định."
            : "Đã xảy ra lỗi máy chủ. Vui lòng thử lại.";

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = "Lỗi máy chủ nội bộ",
            Detail = detail
        });
    });
});

var swaggerEnabled = app.Environment.IsDevelopment() ||
    builder.Configuration.GetValue<bool>("Swagger:Enabled");

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors("FlutterWeb");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "SmartMoney.Api"
})).AllowAnonymous();

app.MapControllers();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

if (firebaseApp != null)
{
    app.Lifetime.ApplicationStopping.Register(() => firebaseApp.Delete());
}

app.Run();