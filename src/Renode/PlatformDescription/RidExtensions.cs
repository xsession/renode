using System;
using System.Linq;
using System.Runtime.InteropServices;

using Antmicro.Renode.Core;

namespace Antmicro.Renode.Utilities
{
    public static class RidExtensions
    {
        /// <summary>
        /// Returns true when the type (or any of its base types) carries no
        /// <see cref="SupportedRIDAttribute"/>, or at least one of the declared
        /// RIDs matches the current platform.
        /// </summary>
        public static bool IsRIDSupported(this Type type)
        {
            var attributes = System.Reflection.CustomAttributeExtensions
                .GetCustomAttributes<SupportedRIDAttribute>(type, inherit: true).ToArray();
            if(attributes.Length == 0)
            {
                return true;
            }
            var current = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" : "unix";
            return attributes.Any(a => string.Equals(a.Rid, current, StringComparison.OrdinalIgnoreCase));
        }
    }
}
