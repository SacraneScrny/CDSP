using System.Collections.Generic;

using CDSP.Utils;

using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace CDSP.Entry
{
    /// <summary>PlayerLoop injection of the CDSP tick loop (after behaviour update/fixed/late). Ticks every registered root with clamped delta; re-initialization is idempotent.</summary>
    public static class EntryPointUpdate
    {
        internal const float DELTATIME_MAX = 0.1f;

        struct CDSPUpdateSystem { }
        struct CDSPFixedUpdateSystem { }
        struct CDSPLateUpdateSystem { }

        /// <summary>Global on/off for all passes.</summary>
        public static bool IsActive { get; private set; }
        /// <summary>Global on/off for the Update pass.</summary>
        public static bool IsUpdateTick { get; private set; }
        /// <summary>Global on/off for the FixedUpdate pass.</summary>
        public static bool IsUpdateFixedTick { get; private set; }
        /// <summary>Global on/off for the LateUpdate pass.</summary>
        public static bool IsUpdateLateTick { get; private set; }

        internal static void Init()
        {
            IsActive = true;
            IsUpdateTick = true;
            IsUpdateFixedTick = true;
            IsUpdateLateTick = true;

            CleanUp();

            var loop = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopHelper.InsertAfter<Update.ScriptRunBehaviourUpdate>(ref loop, new PlayerLoopSystem
            {
                type = typeof(CDSPUpdateSystem),
                updateDelegate = Tick
            });
            PlayerLoopHelper.InsertAfter<FixedUpdate.ScriptRunBehaviourFixedUpdate>(ref loop, new PlayerLoopSystem
            {
                type = typeof(CDSPFixedUpdateSystem),
                updateDelegate = FixedTick
            });
            PlayerLoopHelper.InsertAfter<PreLateUpdate.ScriptRunBehaviourLateUpdate>(ref loop, new PlayerLoopSystem
            {
                type = typeof(CDSPLateUpdateSystem),
                updateDelegate = LateTick
            });
            PlayerLoop.SetPlayerLoop(loop);
        }
        static void CleanUp()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopHelper.Remove<CDSPUpdateSystem>(ref loop);
            PlayerLoopHelper.Remove<CDSPFixedUpdateSystem>(ref loop);
            PlayerLoopHelper.Remove<CDSPLateUpdateSystem>(ref loop);
            PlayerLoop.SetPlayerLoop(loop);
        }

        static void Tick()
        {
            if (!IsActive)  return;
            if (!IsUpdateTick) return;
            var deltaTime = Mathf.Min(Time.deltaTime, DELTATIME_MAX);

            var list = EntryPoint.MainContextsList;
            int initialCount = list.Count;
            int ticked = 0;
            int i = 0;
            while (ticked < initialCount && i < list.Count)
            {
                var context = list[i];
                context.Tick(deltaTime);
                ticked++;
                if (i < list.Count && ReferenceEquals(list[i], context))
                    i++;
            }
        }
        static void FixedTick()
        {
            if (!IsActive)  return;
            if (!IsUpdateFixedTick) return;
            var deltaTime = Mathf.Min(Time.fixedDeltaTime, DELTATIME_MAX);

            var list = EntryPoint.MainContextsList;
            int initialCount = list.Count;
            int ticked = 0;
            int i = 0;
            while (ticked < initialCount && i < list.Count)
            {
                var context = list[i];
                context.FixedTick(deltaTime);
                ticked++;
                if (i < list.Count && ReferenceEquals(list[i], context))
                    i++;
            }
        }
        static void LateTick()
        {
            if (!IsActive)  return;
            if (!IsUpdateLateTick) return;
            var deltaTime = Mathf.Min(Time.deltaTime, DELTATIME_MAX);

            var list = EntryPoint.MainContextsList;
            int initialCount = list.Count;
            int ticked = 0;
            int i = 0;
            while (ticked < initialCount && i < list.Count)
            {
                var context = list[i];
                context.LateTick(deltaTime);
                ticked++;
                if (i < list.Count && ReferenceEquals(list[i], context))
                    i++;
            }
        }

        /// <summary>Pauses/resumes everything.</summary>
        public static void SetActive(bool value)
        {
            IsActive = value;
        }
        /// <summary>Pauses/resumes the Update pass globally.</summary>
        public static void SetUpdateTick(bool value)
        {
            IsUpdateTick = value;
        }
        /// <summary>Pauses/resumes the FixedUpdate pass globally.</summary>
        public static void SetUpdateFixedTick(bool value)
        {
            IsUpdateFixedTick = value;
        }
        /// <summary>Pauses/resumes the LateUpdate pass globally.</summary>
        public static void SetUpdateLateTick(bool value)
        {
            IsUpdateLateTick = value;
        }
    }
}
