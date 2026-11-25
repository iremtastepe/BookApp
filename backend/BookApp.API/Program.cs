using BookApp.Domain.Entities;
using BookApp.Infrastructure;
using BookApp.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using BookApp.Application.Interfaces;
using BookApp.Infrastructure.Services;
using BookApp.Application.Services;
using BookApp.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// 1) Veritabani baglantisi
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// 2) Identity
builder.Services.AddIdentity<User, IdentityRole<int>>(options =>
{
    options.Password.RequiredLength = 6;
    options.Password.RequireDigit = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// 3) JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"]!;

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme =
        JwtBearerDefaults.AuthenticationScheme;

    options.DefaultChallengeScheme =
        JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtKey))
    };
});

// 4) CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddAuthorization();

// 5) Rate Limiting - 7.4-C.5
// Yalnizca ilgili endpointlere uygulanacak iki politika.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode =
        StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode =
            StatusCodes.Status429TooManyRequests;

        await context.HttpContext.Response.WriteAsJsonAsync(
            new
            {
                message = "Çok fazla istek gönderdiniz. " +
                          "Lütfen biraz sonra tekrar deneyin."
            },
            cancellationToken: cancellationToken);
    };

    // Manuel kitap ekleme: kullanici basina dakikada 10 istek.
    options.AddPolicy("BookCreate", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetRateLimitUserKey(context),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    // ISBN aramasi: kullanici basina dakikada 20 istek.
    options.AddPolicy("IsbnSearch", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetRateLimitUserKey(context),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

// 6) DTO/model dogrulama hatalarinda ortak yanit bicimi
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(e => e.Value is { Errors.Count: > 0 })
                .ToDictionary(
                    e => e.Key,
                    e => e.Value!.Errors
                        .Select(x => string.IsNullOrWhiteSpace(x.ErrorMessage)
                            ? "Geçersiz değer."
                            : x.ErrorMessage)
                        .ToArray());

            var message = errors.Values
                .FirstOrDefault()?
                .FirstOrDefault()
                ?? "Geçersiz istek.";

            return new BadRequestObjectResult(
                new { message, errors });
        };
    });

// 7) Servis kayitlari
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IBookRepository, BookRepository>();
builder.Services.AddScoped<IBookService, BookService>();
builder.Services.AddScoped<IUserBookRepository, UserBookRepository>();
builder.Services.AddScoped<IUserBookService, UserBookService>();
builder.Services.AddScoped<IReadingGoalRepository, ReadingGoalRepository>();
builder.Services.AddScoped<IReadingGoalService, ReadingGoalService>();
builder.Services.AddScoped<IStatisticsRepository, StatisticsRepository>();
builder.Services.AddScoped<IStatisticsService, StatisticsService>();

// 8) Infrastructure servisleri
builder.Services.AddInfrastructureServices();

// 9) Swagger UI + JWT destegi
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc(
        "v1",
        new OpenApiInfo
        {
            Title = "BookApp API",
            Version = "v1"
        });

    c.AddSecurityDefinition(
        "Bearer",
        new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Token'ı buraya 'Bearer {token}' formatında gir"
        });

    c.AddSecurityRequirement(
        document => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(
                "Bearer",
                document)] = []
        });
});

var app = builder.Build();

// Kullanici bazli sinirlama icin JWT kullanici kimligini al.
// Kullanici kimligi bulunmazsa anonim istekleri IP bazinda ayir.
static string GetRateLimitUserKey(HttpContext context)
{
    var userId =
        context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? context.User.FindFirst("sub")?.Value;

    if (!string.IsNullOrWhiteSpace(userId))
    {
        return $"user:{userId}";
    }

    return $"anonymous:{context.Connection.RemoteIpAddress}";
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseCors("AllowAll");

// Once JWT kimligini dogrula.
app.UseAuthentication();

// Yetkileri kontrol et.
app.UseAuthorization();

// Kullanici kimligi belirlendikten sonra rate limiting uygula.
app.UseRateLimiter();

app.MapControllers();

// Guvenli Admin rolu ve ilk kurulum kontrolu
await IdentityDataSeeder.SeedAdminAsync(app.Services);

app.Run();
