// NewwaysAdmin.WebAdmin/Services/GoogleSheets/TemplateStorageService.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.WebAdmin.Infrastructure.Storage;
using NewwaysAdmin.WebAdmin.Components.Settings.GoogleSheets;

namespace NewwaysAdmin.WebAdmin.Services.GoogleSheets
{
    public class TemplateStorageService : ITemplateStorageService
    {
        private readonly IDataStorage<Dictionary<string, AdminTemplateBuilder.AdminTemplateConfig>> _templateStorage;
        private readonly ILogger<TemplateStorageService> _logger;
        private const string TEMPLATES_KEY = "admin-templates";

        public TemplateStorageService(
            StorageManager storageManager,
            ILogger<TemplateStorageService> logger)
        {
            _logger = logger;
            _logger.LogInformation("🔧 TemplateStorageService constructor started");

            _templateStorage = storageManager.GetStorageSync<Dictionary<string, AdminTemplateBuilder.AdminTemplateConfig>>("GoogleSheets_Templates");
            _logger.LogInformation("✅ TemplateStorageService constructor completed");
        }

        public async Task<Dictionary<string, AdminTemplateBuilder.AdminTemplateConfig>> LoadAllTemplatesAsync()
        {
            _logger.LogInformation("🔄 LoadAllTemplatesAsync started");

            try
            {
                _logger.LogInformation("📂 Loading templates with key: {Key}", TEMPLATES_KEY);
                var templates = await _templateStorage.LoadAsync(TEMPLATES_KEY);

                if (templates == null)
                {
                    _logger.LogInformation("📭 No templates found, returning empty dictionary");
                    return new Dictionary<string, AdminTemplateBuilder.AdminTemplateConfig>();
                }

                _logger.LogInformation("📚 Found {Count} templates in storage", templates.Count);
                return templates;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in LoadAllTemplatesAsync");
                return new Dictionary<string, AdminTemplateBuilder.AdminTemplateConfig>();
            }
        }

        public async Task<AdminTemplateBuilder.AdminTemplateConfig?> LoadTemplateAsync(string moduleName)
        {
            _logger.LogInformation("🔄 LoadTemplateAsync started for module: {ModuleName}", moduleName);

            try
            {
                var templates = await LoadAllTemplatesAsync();
                return templates.TryGetValue(moduleName, out var template) ? template : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error loading template for module {ModuleName}", moduleName);
                return null;
            }
        }

        public async Task<bool> SaveTemplateAsync(AdminTemplateBuilder.AdminTemplateConfig template)
        {
            _logger.LogInformation("💾 SaveTemplateAsync started for module: {ModuleName}", template.ModuleName);

            try
            {
                // Load existing templates
                _logger.LogInformation("📂 Loading existing templates...");
                var templates = await LoadAllTemplatesAsync();

                // Update the template
                _logger.LogInformation("📝 Adding/updating template for module: {ModuleName}", template.ModuleName);
                templates[template.ModuleName] = template;

                // Save back to storage
                _logger.LogInformation("💾 Saving templates to storage with key: {Key}", TEMPLATES_KEY);
                await _templateStorage.SaveAsync(TEMPLATES_KEY, templates);

                _logger.LogInformation("✅ Template saved successfully for module: {ModuleName}", template.ModuleName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error saving template for module {ModuleName}", template.ModuleName);
                return false;
            }
        }

        public async Task<bool> DeleteTemplateAsync(string moduleName)
        {
            _logger.LogInformation("🗑️ DeleteTemplateAsync started for module: {ModuleName}", moduleName);

            try
            {
                // Load existing templates
                _logger.LogInformation("📂 Loading existing templates...");
                var templates = await LoadAllTemplatesAsync();

                // Remove the template
                _logger.LogInformation("🗑️ Removing template for module: {ModuleName}", moduleName);
                var removed = templates.Remove(moduleName);

                if (removed)
                {
                    // Save back to storage
                    _logger.LogInformation("💾 Saving updated templates to storage...");
                    await _templateStorage.SaveAsync(TEMPLATES_KEY, templates);
                    _logger.LogInformation("✅ Template deleted successfully for module: {ModuleName}", moduleName);
                }
                else
                {
                    _logger.LogInformation("ℹ️ No template found to delete for module: {ModuleName}", moduleName);
                }

                return removed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting template for module {ModuleName}", moduleName);
                return false;
            }
        }

        public async Task<List<string>> GetAvailableModulesWithTemplatesAsync()
        {
            _logger.LogInformation("📋 GetAvailableModulesWithTemplatesAsync started");

            try
            {
                var templates = await LoadAllTemplatesAsync();
                var modules = templates.Keys.ToList();
                _logger.LogInformation("📋 Found {Count} modules with templates", modules.Count);
                return modules;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting available modules with templates");
                return new List<string>();
            }
        }
    }
}