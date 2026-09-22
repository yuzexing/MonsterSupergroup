using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>The owner's measured camera footprint. Never substitutes a view centred on a remote avatar.</summary>
    public struct PlayerViewReport
    {
        public const double MaximumAge = .5;
        public uint Round, Sequence;
        public double SampledAt;
        public Vector2 Center, Size;
        public Bounds Bounds => new Bounds(Center, new Vector3(Size.x, Size.y, 0));
        public bool IsFresh(uint round, double now) => Sequence != 0 && Round == round &&
            double.IsFinite(now) && double.IsFinite(SampledAt) && now - SampledAt <= MaximumAge && SampledAt - now <= .25;
        public bool IsValid(uint round, double now) => IsFresh(round, now) &&
            float.IsFinite(Center.x) && float.IsFinite(Center.y) && Mathf.Abs(Center.x) <= 1000000 && Mathf.Abs(Center.y) <= 1000000 &&
            float.IsFinite(Size.x) && float.IsFinite(Size.y) && Size.x >= .1f && Size.x <= 1000 && Size.y >= .1f && Size.y <= 1000;
        public static bool Intersects(Bounds view, Bounds body) =>
            Mathf.Abs(body.center.x - view.center.x) <= view.extents.x + body.extents.x &&
            Mathf.Abs(body.center.y - view.center.y) <= view.extents.y + body.extents.y;
    }
}
