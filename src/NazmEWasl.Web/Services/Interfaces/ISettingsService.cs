namespace NazmEWasl.Web.Services.Interfaces;

public interface ISettingsService
{
    string Get(string key, string defaultValue = "");
    void Set(string key, string value);
    Task SaveAsync();
}
