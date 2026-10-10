using System.Collections.Generic;

using UnityEngine.LowLevel;

namespace CDSP.Utils
{
    internal static class PlayerLoopHelper
    {
        public static bool InsertAfter<TTarget>(ref PlayerLoopSystem loop, PlayerLoopSystem toInsert)
        {
            if (loop.subSystemList == null) return false;

            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type == typeof(TTarget))
                {
                    var list = new List<PlayerLoopSystem>(loop.subSystemList);
                    list.Insert(i + 1, toInsert);
                    loop.subSystemList = list.ToArray();
                    return true;
                }

                var sub = loop.subSystemList[i];
                if (InsertAfter<TTarget>(ref sub, toInsert))
                {
                    loop.subSystemList[i] = sub;
                    return true;
                }
            }

            return false;
        }

        public static void Remove<TTarget>(ref PlayerLoopSystem loop)
        {
            if (loop.subSystemList == null) return;

            var list = new List<PlayerLoopSystem>(loop.subSystemList);
            int removed = list.RemoveAll(s => s.type == typeof(TTarget));
            if (removed > 0)
            {
                loop.subSystemList = list.ToArray();
                return;
            }

            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                var sub = loop.subSystemList[i];
                Remove<TTarget>(ref sub);
                loop.subSystemList[i] = sub;
            }
        }
    }
}