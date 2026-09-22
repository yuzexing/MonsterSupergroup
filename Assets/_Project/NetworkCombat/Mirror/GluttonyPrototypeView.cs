using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using UnityEngine;
namespace MonsterSupergroup.NetworkCombat
{
    // Presentation only: never modifies a real enemy's transform, collider, HP or movement.
    public sealed partial class GluttonyPrototypeView : MonoBehaviour
    {
        [SerializeField] private Material prototypeMaterial;
        private NetworkPlayerGluttony skill;
        private GameObject root;
        private Sprite square;
        private AudioSource audioSource;
        private AudioClip castSound, eatSound;
        private readonly Dictionary<uint, SpriteRenderer> markers = new Dictionary<uint, SpriteRenderer>();
        private readonly List<uint> expiredMarkers = new List<uint>();
        private readonly List<Particle> particles = new List<Particle>();
        private ulong lastCast, lastEat;
        private float lastAudio;
        private sealed class Particle { public SpriteRenderer Renderer; public Vector3 Origin; public float Born, Duration; public bool Fly; }
        private void Awake() => skill = GetComponent<NetworkPlayerGluttony>();
        private void EnsureVisuals()
        {
            if (root != null) return;
            root = new GameObject("Gluttony prototype visuals"); root.hideFlags = HideFlags.DontSave;
            square = Sprite.Create(Texture2D.whiteTexture, new Rect(0,0,Texture2D.whiteTexture.width,Texture2D.whiteTexture.height),
                new Vector2(.5f,.5f), Texture2D.whiteTexture.width);
            audioSource = root.AddComponent<AudioSource>(); audioSource.playOnAwake = false; audioSource.spatialBlend = 0;
            castSound = Tone("Prototype mark", 320, 650); eatSound = Tone("Prototype collect", 800, 420);
        }
        private static AudioClip Tone(string name, float from, float to)
        {
            const int rate = 22050, count = 2205;
            var data = new float[count]; float phase = 0;
            for (int i=0;i<count;i++) { float t=(float)i/count; phase += 2*Mathf.PI*Mathf.Lerp(from,to,t)/rate; data[i]=Mathf.Sin(phase)*Mathf.Sin(t*Mathf.PI)*.4f; }
            var clip = AudioClip.Create(name,count,1,rate,false); clip.SetData(data,0); return clip;
        }
        private SpriteRenderer Shape(string name, Vector3 position, Vector3 scale, Color color)
        {
            EnsureVisuals(); var go = new GameObject(name); go.transform.SetParent(root.transform,false);
            go.transform.position = position; go.transform.localScale = scale;
            var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = square;
            if (prototypeMaterial != null) renderer.sharedMaterial = prototypeMaterial;
            renderer.sortingLayerID = SortingLayer.NameToID("Foreground");
            renderer.sortingOrder = 32000; renderer.color = color; return renderer;
        }
        public void ShowRectangle(ulong id, Vector2 origin, Vector2 direction, GluttonyParameters settings)
        {
            if (!isActiveAndEnabled || id == lastCast) return; lastCast = id;
            var r = Shape("Mark rectangle",origin+direction*settings.Length*.5f,new Vector3(settings.Length,settings.Width,1),new Color(.35f,1,.3f,.22f));
            r.transform.rotation=Quaternion.Euler(0,0,Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg);
            particles.Add(new Particle { Renderer=r, Born=Time.unscaledTime, Duration=settings.FlashDuration });
            audioSource.PlayOneShot(castSound,settings.FeedbackVolume);
        }
        public void ShowDevour(ulong id, Vector2 position, float volume)
        {
            if (!isActiveAndEnabled || id == lastEat) return; lastEat = id;
            var r=Shape("Collected mote",position,Vector3.one*.22f,new Color(.9f,1,.25f,1));
            particles.Add(new Particle { Renderer=r, Origin=position, Born=Time.unscaledTime, Duration=.22f, Fly=true });
            if (Time.unscaledTime-lastAudio>.025f) { audioSource.PlayOneShot(eatSound,volume); lastAudio=Time.unscaledTime; }
        }
        private void LateUpdate()
        {
            if (skill == null || !skill.isClient) return;
            UpdateRange();
            expiredMarkers.Clear(); foreach (uint id in markers.Keys) expiredMarkers.Add(id);
            if (skill.IsPrototypeEnabled && skill.Parameters.Enabled && NetworkTime.time<skill.State.MarkExpiresAt)
                foreach (uint id in skill.MarkedTargets)
                {
                    if (!NetworkClient.spawned.TryGetValue(id,out var obj) || obj == null) continue;
                    var enemy=obj.GetComponent<EnemyController>(); if (enemy == null || !enemy.IsAlive) continue;
                    if (!markers.TryGetValue(id,out var marker))
                    {
                        marker=Shape("Devour mark",obj.transform.position,Vector3.one*.24f,Color.white);
                        marker.transform.rotation=Quaternion.Euler(0,0,45); markers.Add(id,marker);
                    }
                    expiredMarkers.Remove(id);
                    marker.transform.position=enemy.OnHitEffectTopPivot != null ? enemy.OnHitEffectTopPivot.position+Vector3.up*.3f : obj.transform.position+Vector3.up*1.4f;
                    float alpha=skill.State.MarkExpiresAt-NetworkTime.time<1 && Mathf.Repeat(Time.unscaledTime*6,1)<.5f ? .25f : 1;
                    marker.color=skill.isOwned ? new Color(.75f,1,.1f,alpha) : new Color(.55f,.65f,.8f,alpha*.65f);
                }
            foreach (uint id in expiredMarkers) { if (markers[id]!=null) Destroy(markers[id].gameObject); markers.Remove(id); }
            for (int i=particles.Count-1;i>=0;i--)
            {
                var particle=particles[i]; float t=(Time.unscaledTime-particle.Born)/particle.Duration;
                if (particle.Renderer==null || t>=1)
                { if (particle.Renderer!=null) Destroy(particle.Renderer.gameObject); particles.RemoveAt(i); continue; }
                if (particle.Fly)
                {
                    particle.Renderer.transform.position=Vector3.Lerp(particle.Origin,transform.position+Vector3.up*.4f,t*t);
                    particle.Renderer.transform.localScale=Vector3.one*Mathf.Lerp(.22f,.06f,t);
                }
            }
        }
        public void ClearVisuals()
        {
            markers.Clear(); particles.Clear();
            if (root!=null) Destroy(root); root=null;
            if (square!=null) Destroy(square); square=null;
            if (castSound!=null) Destroy(castSound); castSound=null;
            if (eatSound!=null) Destroy(eatSound); eatSound=null;
        }
        private void OnDisable() => ClearVisuals();
        private void OnDestroy() => ClearVisuals();
    }
}
