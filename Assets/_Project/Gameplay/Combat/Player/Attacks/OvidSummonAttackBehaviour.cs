using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
    public class OvidSummonAttackBehaviour : SummonAttackBehaviour
    {
        // Source prefab overrides this source-code default to 60 seconds.
        [SerializeField] private float cacoonStateTime = 10f;
        public override float InitialMaturityDelay => cacoonStateTime;

        public override SummonPhase GetMaturityPhase(out float elapsed)
        {
            double now = ClockNow;
            if (now < MaturityAt)
            {
                elapsed = (float)Math.Max(0d, now - (MaturityAt - InitialMaturityDelay));
                return SummonPhase.Cocoon;
            }
            double birthAge = now - MaturityAt;
            elapsed = (float)birthAge;
            return birthAge < BirthPresentationDuration ? SummonPhase.Birth : SummonPhase.Positioning;
        }
    }
}