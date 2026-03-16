using Microsoft.EntityFrameworkCore;
using NazmEWasl.Web.Data;
using NazmEWasl.Web.Services;
using NazmEWasl.Web.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddScoped<IStorageService, LocalStorageService>();
builder.Services.AddScoped<IPipelineService, PipelineService>();
builder.Services.AddHttpClient<ILrcLibService, LrcLibService>();

builder.Services.AddHttpClient("Gemini", client =>
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/"));

builder.Services.AddHttpClient("Anthropic", client =>
{
    client.BaseAddress = new Uri("https://api.anthropic.com/");
    client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    // API key is injected per-request by the batch services (using x-api-key header)
});

builder.Services.AddHttpClient("OpenAI", client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/");
    // Authorization header added per-request by the batch services
});

// Translation services
builder.Services.AddScoped<TranslationService>();
builder.Services.AddScoped<GeminiTranslationService>();
builder.Services.AddScoped<OpenAITranslationService>();
builder.Services.AddScoped<ITranslationServiceFactory, TranslationServiceFactory>();

// Batch translation
builder.Services.AddScoped<ClaudeBatchTranslationService>();
builder.Services.AddScoped<OpenAiBatchTranslationService>();
builder.Services.AddScoped<BatchTranslationServiceFactory>();

// Settings
builder.Services.AddScoped<ISettingsService, DbSettingsService>();

// Cancellation registry (singleton so it persists across requests)
builder.Services.AddSingleton<CancellationRegistry>();

// Background pipeline queue (singleton channel + hosted executors)
builder.Services.AddSingleton<IBackgroundPipelineQueue, BackgroundPipelineQueue>();
builder.Services.AddHostedService<PipelineHostedService>();
builder.Services.AddHostedService<BatchPollingHostedService>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Songs}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
