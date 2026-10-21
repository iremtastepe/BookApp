
using BookApp.Domain.Entities;
using BookApp.Infrastructure;
using BookApp.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;
using BookApp.Application.Interfaces;
using BookApp.Infrastructure.Services;
using BookApp.Application.Services;
using BookApp.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// 1) Veritabanı bağlantısı
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2) Identity (kullanıcı yönetimi: kayıt, şifre hashleme vs.)
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
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
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
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

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

// 6.6 Adım 2b: DTO/model doğrulama hatalarında ortak yanıt biçimi
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

            var message = errors.Values.FirstOrDefault()?.FirstOrDefault()
                ?? "Geçersiz istek.";

            return new BadRequestObjectResult(new { message, errors });
        };
    });

// Servis kayıtları (DI)
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IBookRepository, BookRepository>();
builder.Services.AddScoped<IBookService, BookService>();
builder.Services.AddScoped<IUserBookRepository, UserBookRepository>();
builder.Services.AddScoped<IUserBookService, UserBookService>();
builder.Services.AddScoped<IReadingGoalRepository, ReadingGoalRepository>();
builder.Services.AddScoped<IReadingGoalService, ReadingGoalService>();
builder.Services.AddScoped<IStatisticsRepository, StatisticsRepository>();
builder.Services.AddScoped<IStatisticsService, StatisticsService>();

// Altyapı (Infrastructure) servislerimizi kaydediyoruz (İşçiler ve Şef)
builder.Services.AddInfrastructureServices();

// 4) Swagger UI + JWT desteği (Authorize butonu için)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "BookApp API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Token'ı buraya 'Bearer {token}' formatında gir"
    });

    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowAll"); // CORS politikasını Auth'dan hemen önce etkinleştir

app.UseAuthentication();  // Önce "sen kimsin" kontrolü
app.UseAuthorization();   // Sonra "bunu yapmaya yetkin var mı" kontrolü

app.MapControllers();

app.Run();
