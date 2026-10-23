using JtsCommandCenter.Infrastructure;
using JtsCommandCenter.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddWeather(builder.Configuration);
builder.Services.AddSports(builder.Configuration);
builder.Services.AddProjects(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Keeps the LocalDB schema current. Deployed environments are migrated by the pipeline.
    await app.Services.MigrateDatabaseAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
