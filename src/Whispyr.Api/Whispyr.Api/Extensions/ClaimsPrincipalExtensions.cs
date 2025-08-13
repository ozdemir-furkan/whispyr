using System.Security.Claims;

namespace Whispyr.Api.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        public static string? GetUserId(this ClaimsPrincipal user)
        {
            // JWT’de NameIdentifier (nameid) veya "sub" kullanılıyor olabilir.
            return user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub");
        }
    }
}