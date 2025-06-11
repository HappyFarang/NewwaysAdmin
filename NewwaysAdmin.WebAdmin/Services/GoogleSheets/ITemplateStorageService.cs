// NewwaysAdmin.WebAdmin/Services/GoogleSheets/ITemplateStorageService.cs
using NewwaysAdmin.WebAdmin.Components.Settings.GoogleSheets;

namespace NewwaysAdmin.WebAdmin.Services.GoogleSheets
{
    public interface ITemplateStorageService
    {
        Task<Dictionary<string, AdminTemplateBuilder.AdminTemplateConfig>> LoadAllTemplatesAsync();
        Task<AdminTemplateBuilder.AdminTemplateConfig?> LoadTemplateAsync(string moduleName);
        Task<bool> SaveTemplateAsync(AdminTemplateBuilder.AdminTemplateConfig template);
        Task<bool> DeleteTemplateAsync(string moduleName);
        Task<List<string>> GetAvailableModulesWithTemplatesAsync();
    }
}