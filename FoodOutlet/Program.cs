using FoodOutlet.AppCode;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    var keyDir = Path.Combine(Path.GetTempPath(), "FoodOutlet-dp-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(keyDir);
    builder.Services.AddDataProtection()
        .SetApplicationName("FoodOutlet-dev")
        .PersistKeysToFileSystem(new DirectoryInfo(keyDir));
}

builder.Services.AddScoped<IDbConnectionFactory, MySqlConnectionFactory>();
builder.Services.AddScoped<Staff>();
builder.Services.AddScoped<ImageProcessingService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOrChef", policy =>
        policy.RequireAssertion(ctx =>
        {
            var r = ctx.User.FindFirstValue(ClaimTypes.Role)
                ?? ctx.User.FindFirst("RoleName")?.Value;
            if (string.IsNullOrWhiteSpace(r)) return false;
            r = r.Trim();
            return r.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                || r.Equals("Chef", StringComparison.OrdinalIgnoreCase);
        }));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Map controller routes with attribute routing (for [HttpGet("table/{tableNumber}")])
app.MapControllers();

// Map default route
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

if (app.Environment.IsDevelopment())
{
    app.MapPost("/__debug/browser-log", async (HttpContext ctx, IWebHostEnvironment env) =>
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var body = (await reader.ReadToEndAsync()).Trim();
        if (string.IsNullOrEmpty(body))
            return Results.BadRequest();
        var logPath = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "debug-417eff.log"));
        await File.AppendAllTextAsync(logPath, body + Environment.NewLine);
        return Results.Ok();
    });
}

app.Run();
