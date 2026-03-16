using NazmEWasl.Web.Data;
using NazmEWasl.Web.Models.Domain;
using NazmEWasl.Web.Services.Interfaces;

namespace NazmEWasl.Web.Services;

public class DbSettingsService : ISettingsService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly Dictionary<string, string> _cache = new();
    private bool _loaded = false;

    public DbSettingsService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        foreach (var s in _db.AppSettings)
            _cache[s.Key] = s.Value;
        _loaded = true;
    }

    public string Get(string key, string defaultValue = "")
    {
        EnsureLoaded();
        if (_cache.TryGetValue(key, out var cached)) return cached;
        // Fall back to IConfiguration (appsettings.json uses colon path, settings use dot — map both)
        var configKey = key.Replace('.', ':');
        var fromConfig = _config[configKey];
        return fromConfig ?? defaultValue;
    }

    public void Set(string key, string value)
    {
        EnsureLoaded();
        _cache[key] = value;
    }

    public async Task SaveAsync()
    {
        EnsureLoaded();
        foreach (var (key, value) in _cache)
        {
            var existing = await _db.AppSettings.FindAsync(key);
            if (existing == null)
                _db.AppSettings.Add(new AppSetting { Key = key, Value = value });
            else
                existing.Value = value;
        }
        await _db.SaveChangesAsync();
    }
}
