using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MonsterSupergroup.NordicSample
{
    public sealed class NordicTorchAmbient : MonoBehaviour
    {
        public Transform flame;
        public Light2D[] lights;
        private Vector3 flameScale;
        private float[] intensities;
        private void Awake()
        {
            flameScale = flame != null ? flame.localScale : Vector3.one;
            intensities = new float[lights.Length];
            for (int i = 0; i < lights.Length; i++) intensities[i] = lights[i].intensity;
        }
        private void Update()
        {
            float phase = Time.time * 8.1f + transform.position.x * 0.7f;
            float pulse = Mathf.Sin(phase) * 0.035f + Mathf.Sin(phase * 1.71f) * 0.02f;
            if (flame != null) flame.localScale = Vector3.Scale(flameScale, new Vector3(1 - pulse * .5f, 1 + pulse, 1));
            for (int i = 0; i < lights.Length; i++) lights[i].intensity = intensities[i] * (1 + pulse);
        }
    }
}
