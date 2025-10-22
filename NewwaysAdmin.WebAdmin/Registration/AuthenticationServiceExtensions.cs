// File: NewwaysAdmin.WebAdmin/Registration/AuthenticationServiceExtensions.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using NewwaysAdmin.WebAdmin.Services.Auth;
using NewwaysAdmin.WebAdmin.Authentication;
using NewwaysAdmin.WebAdmin.Authorization;
using NewwaysAdmin.WebAdmin.Services.Circuit;
using NewwaysAdmin.WebAdmin.Services.Security;
using NewwaysAdmin.WebAdmin.Services.Navigation;
using NewwaysAdmin.SharedModels.Config;
using NewwaysAdmin.WebAdmin.Models.Auth;

namespace NewwaysAdmin.WebAdmin.Registration
{
    public static class AuthenticationServiceExtensions
    {
        public static IServiceCollection AddAuthenticationServices(this IServiceCollection services)
        {
            // ===== HTTP CONTEXT ACCESSOR (Required for web apps) =====
            services.AddHttpContextAccessor();

            // ===== SECURITY SERVICES =====
            // Simple DoS protection service (uses StorageManager from previous layer)
            services.AddSingleton<ISimpleDoSProtectionService, SimpleDoSProtectionService>();

            // User initialization service
            services.AddScoped<UserInitializationService>();

            // ===== AUTHORIZATION POLICIES =====
            services.AddAuthorizationCore(options =>
            {
                // Create policies for each module and access level combination
                var modules = new[] { "home", "test", "settings", "sales", "accounting", "accounting.bankslips" };
                var accessLevels = new[] { AccessLevel.Read, AccessLevel.ReadWrite };

                foreach (var module in modules)
                {
                    foreach (var level in accessLevels)
                    {
                        // Module policies
                        options.AddPolicy($"Module_{module}_{level}", policy =>
                            policy.Requirements.Add(new ModuleAccessRequirement(module, level)));

                        // Page policies
                        options.AddPolicy($"Page_{module}_{level}", policy =>
                            policy.Requirements.Add(new PageAccessRequirement(module, level)));
                    }
                }

                // Admin only policy
                options.AddPolicy("AdminOnly", policy =>
                    policy.RequireRole("Admin"));
            });

            // ===== AUTHORIZATION HANDLERS =====
            services.AddScoped<IAuthorizationHandler, ModuleAccessHandler>();
            services.AddScoped<IAuthorizationHandler, PageAccessHandler>();

            // ===== CIRCUIT HANDLING =====
            services.AddScoped<CircuitHandler, CustomCircuitHandler>();
            services.AddSingleton<ICircuitManager, CircuitManager>();

            // ===== AUTHENTICATION & NAVIGATION =====
            services.AddScoped<IAuthenticationService, AuthenticationService>();
            services.AddScoped<INavigationService, NavigationService>();
            services.AddScoped<AuthenticationStateProvider, CustomAuthenticationStateProvider>();

            return services;
        }
    }
}