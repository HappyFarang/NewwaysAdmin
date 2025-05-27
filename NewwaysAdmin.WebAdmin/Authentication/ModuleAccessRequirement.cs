using Microsoft.AspNetCore.Authorization;
using NewwaysAdmin.WebAdmin.Models.Auth;

namespace NewwaysAdmin.WebAdmin.Authorization
{
    public class ModuleAccessRequirement : IAuthorizationRequirement
    {
        public string ModuleId { get; }
        public AccessLevel MinimumAccessLevel { get; }

        public ModuleAccessRequirement(string moduleId, AccessLevel minimumAccessLevel = AccessLevel.Read)
        {
            ModuleId = moduleId;
            MinimumAccessLevel = minimumAccessLevel;
        }
    }

    public class PageAccessRequirement : IAuthorizationRequirement
    {
        public string PageId { get; }
        public AccessLevel MinimumAccessLevel { get; }

        public PageAccessRequirement(string pageId, AccessLevel minimumAccessLevel = AccessLevel.Read)
        {
            PageId = pageId;
            MinimumAccessLevel = minimumAccessLevel;
        }
    }
}
