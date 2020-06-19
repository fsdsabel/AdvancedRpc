using System.Collections.Generic;
using System.Linq;

namespace AdvancedRpcLib.Helpers
{
#if NETSTANDARD2_0
    static class HashSetExtension
    {
        public static bool TryGetValue<T>(this HashSet<T> hashSet, T equalValue, out T actualValue)
        {
            if (hashSet.Contains(equalValue))
            {
                actualValue = hashSet.First(e => e.Equals(equalValue));
                return true;
            }

            actualValue = default;
            return false;
        }
    }
#endif
}
