using ModernVintageGUI.Designer;
using ModernVintageGUI.Designer.Components;
using ModernVintageGUI.Designer.Rendering;

// Cairo lives in the Vintage Story Lib folder, so the P/Invokes have to be pointed at it before
// anything tries to measure text. Exactly what ZLayoutHarness does, and for the same reason.
NativeCairo.Register();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// One session per browser connection: the document being edited is not shared.
builder.Services.AddScoped<DesignerSession>();
builder.Services.AddSingleton<TemplateLibrary>();

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
