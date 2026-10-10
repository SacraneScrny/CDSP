using CDSP.Base;
using CDSP.Components;
using UnityEngine;

namespace CDSP.Samples
{
    public sealed class CounterDomain : Domain
    {
        public float Timer;
        public int Seconds;
    }

    public sealed class CounterPresentation : Presentation<CounterSystem>
    {
        public void ShowSeconds(int seconds)
        {
            Debug.Log($"Seconds: {seconds}");
        }
    }

    public sealed class CounterSystem : System<CounterDomain, CounterPresentation>
    {
        protected override void OnTick(float deltaTime)
        {
            Domain.Timer += deltaTime;
            if (Domain.Timer < 1f) return;
            Domain.Timer -= 1f;
            Domain.Seconds++;
            Presentation.ShowSeconds(Domain.Seconds);
        }
    }

    public sealed class CounterContext : Context<CounterDomain, CounterSystem, CounterPresentation> { }

    // Scene root: empty GameObject + this component, no prefab needed.
    public sealed class CounterRoot : ContextRoot<CounterContext> { }
}
