using System;

namespace Antmicro.Renode.Core
{
    /// <summary>
    /// Marks an API entry point as supported only on the given runtime
    /// identifier(s), e.g. "unix" for Unix-only backends. Callers that must
    /// remain cross-platform should check the runtime at call time.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
    public class SupportedRIDAttribute : Attribute
    {
        public string Rid { get; }

        public SupportedRIDAttribute(string rid)
        {
            Rid = rid;
        }
    }
}
