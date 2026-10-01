using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using TimePlanner.Core.Data;
using TimePlanner.Core.Extensions;
using TimePlanner.Dashboard.Data;
using TimePlanner.Dashboard.Data.SqlServer;
using TimePlanner.Dashboard.Services;
using TimePlanner.Dashboard.Services.Reports;
using TimePlanner.Dashboard.Services.TimesheetImport;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

// Add services to the container.
builder.Services.AddControllersWithViews();

//sqlite locally and in tests while azure sets SqlServer
var useSqlServer = string.Equals(builder.Configuration["Database:Provider"], "SqlServer", StringComparison.OrdinalIgnoreCase);

//each provider keeps its own context types and migrations
//identity keeps its own history table either way
if (useSqlServer)
{
    builder.Services.AddTimePlannerCore<SqlServerAppDbContext>(o => o.UseSqlServer(connectionString));
    builder.Services.AddDbContext<AuthDbContext, SqlServerAuthDbContext>(options =>
        options.UseSqlServer(connectionString, s => s.MigrationsHistoryTable("__AuthMigrationHistory")));
}
else
{
    builder.Services.AddTimePlannerCore(connectionString);
    builder.Services.AddDbContext<AuthDbContext>(options =>
        options.UseSqlite(connectionString, s => s.MigrationsHistoryTable("__AuthMigrationHistory")));
}

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
{
    o.Password.RequiredLength = 12;
    o.Password.RequireLowercase = true;
    o.Password.RequireUppercase = true;
    o.Password.RequireNonAlphanumeric = true;
    o.Lockout.AllowedForNewUsers = true;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    o.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<AuthDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.SlidingExpiration = true;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
});

// API clients authenticate with a bearer token, the pages keep using the cookie above. AddIdentity already set the
// cookie as the default scheme, so api controllers opt in with AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme.
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<TimesheetImportService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddAuthentication().AddJwtBearer(o =>
{
    // keep the short claim names ("email", "role") instead of renaming them to long schema urls
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = JwtTokenService.KeyFrom(builder.Configuration),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "email",
        RoleClaimType = "role"
    };
});

// Secure by default: every endpoint requires a login unless it opts out with [AllowAnonymous].
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// OpenAPI document and Swagger UI for the /api endpoints, with a Bearer token box so they can be tried from the browser.
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "TimePlanner API", Version = "v1" });
    o.DocInclusionPredicate((_, api) => api.RelativePath?.StartsWith("api/") == true);
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Get a token from POST /api/v1/auth/login, then paste it here."
    });
    o.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

var app = builder.Build();

// The schema is created by EF Core migrations on launch, so no manual setup script is needed.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.Migrate();
    await IdentitySeeder.SeedAsync(scope.ServiceProvider, app.Configuration);
}

// Configure the HTTP request pipeline.
// Api failures are always json problem details and never a stack trace or the html error page, in every environment.
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/api"), api => api.UseExceptionHandler(errors => errors.Run(async ctx =>
{
    var error = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Api").LogError(error, "Unhandled api error");
    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await ctx.Response.WriteAsJsonAsync(new ProblemDetails
    {
        Status = StatusCodes.Status500InternalServerError,
        Title = "An unexpected error occurred.",
        Extensions = { ["traceId"] = ctx.TraceIdentifier }
    });
})));

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// the docs page is served before routing and authorization, so the secure-by-default policy does not redirect it to the login page
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Api:EnableDocs"))
{
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "TimePlanner API v1"));
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// API docs are on in Development, and in other environments only when Api:EnableDocs is set.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Api:EnableDocs"))
{
    // anonymous on purpose: the page only describes the api, every call made from it still needs a bearer token
    app.MapSwagger().AllowAnonymous();
}

// Anonymous liveness check for the host and the deployment pipeline. Reports only up or down, never details.
app.MapGet("/health", async (AppDbContext db) =>
        await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

// exposes the top level Program class to the api test project (WebApplicationFactory<Program>)
public partial class Program { }
