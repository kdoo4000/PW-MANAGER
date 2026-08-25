using System;
using System.Text.RegularExpressions;

namespace PWManager.Domain.Identifiers
{
    public static class EntityId
    {
        private static readonly Regex StaticIdPattern = new(
            "^[a-z][a-z0-9]*_[0-9]{3}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string CreateRuntimeId()
        {
            return Guid.NewGuid().ToString("D");
        }

        public static bool IsValidRuntimeId(string id)
        {
            return Guid.TryParseExact(id, "D", out _);
        }

        public static bool IsValidStaticId(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && StaticIdPattern.IsMatch(id);
        }
    }
}
