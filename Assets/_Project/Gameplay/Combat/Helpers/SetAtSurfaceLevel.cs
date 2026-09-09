using UnityEngine;

namespace AstralShift.Helpers
{
    public class SetAtSurfaceLevel : MonoBehaviour
    {
        [SerializeField] private bool updateOnTick;
        private Transform owner;

        public void BindOwner(Transform value)
        {
            owner = value;
            Apply(false);
        }

        public void UnbindOwner() => owner = null;
        public void RefreshSurface() => Apply(false);
        private void Start() => Apply(false);
        private void LateUpdate() { if (updateOnTick) Apply(true); }

        private void Apply(bool followParent)
        {
            if (owner == null) return;
            Vector3 position = followParent && transform.parent != null ? transform.parent.position : transform.position;
            position.z = owner.position.z;
            transform.position = position;
        }
    }
}
