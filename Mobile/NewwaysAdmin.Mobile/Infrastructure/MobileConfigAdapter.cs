// File: Mobile/NewwaysAdmin.Mobile/Infrastructure/MobileConfigAdapter.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.Configuration;

namespace NewwaysAdmin.Mobile.Infrastructure
{
    /// <summary>
    /// Adapter that wraps MobileMachineConfigProvider to work with IOManager's expected MachineConfigProvider interface
    /// </summary>
    public class MobileConfigAdapter : MachineConfigProvider
    {
        private readonly MobileMachineConfigProvider _mobileProvider;

        public MobileConfigAdapter(ILogger<MachineConfigProvider> logger, MobileMachineConfigProvider mobileProvider)
            : base(logger)
        {
            _mobileProvider = mobileProvider;
        }

        public new async Task<MachineConfig> LoadConfigAsync()
        {
            // Delegate to our mobile provider
            return await _mobileProvider.LoadConfigAsync();
        }
    }
}