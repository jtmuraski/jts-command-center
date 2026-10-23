using JtsCommandCenter.Core.Projects;
using JtsCommandCenter.Infrastructure;
using JtsCommandCenter.Web.Components;
using JtsCommandCenter.Web.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddWeather(builder.Configuration);
builder.Services.AddSports(builder.Configuration);
builder.Services.AddNews(builder.Configuration);
builder.Services.AddProjects(builder.Configuration);
builder.Services.AddScoped<JtsCommandCenter.Web.Components.Projects.ProjectChangeNotifier>();

// A relative local attachments folder lives under the app, not wherever the process was started from.
builder.Services.PostConfigure<AttachmentOptions>(options =>
    options.LocalPath = Path.Combine(builder.Environment.ContentRootPath, options.LocalPath));

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
app.MapAttachmentEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
