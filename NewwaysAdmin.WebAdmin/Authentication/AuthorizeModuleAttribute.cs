using Microsoft.AspNetCore.Authorization;
using NewwaysAdmin.WebAdmin.Models.Auth;

namespace NewwaysAdmin.WebAdmin.Authorization
{
    public class AuthorizeModuleAttribute : AuthorizeAttribute
    {
        public AuthorizeModuleAttribute(string moduleId, AccessLevel minimumLevel = AccessLevel.Read)
        {
            Policy = $"Module_{moduleId}_{minimumLevel}";
        }
    }

    public class AuthorizePageAttribute : AuthorizeAttribute
    {
        public AuthorizePageAttribute(string pageId, AccessLevel minimumLevel = AccessLevel.Read)
        {
            Policy = $"Page_{pageId}_{minimumLevel}";
        }
    }
}
