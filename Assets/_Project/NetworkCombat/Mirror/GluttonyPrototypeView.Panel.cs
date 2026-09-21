using System;
using Mirror;
using UnityEngine;
namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class GluttonyPrototypeView
    {
        public bool PanelOpen { get; private set; }
        private GluttonyParameters draft;
        private void OnGUI()
        {
            if (skill==null || !skill.isOwned || !skill.isClient) return;
            if (GUI.Button(new Rect(Screen.width-300,Screen.height-44,290,34),PanelOpen ? "Close Gluttony panel" : "Gluttony prototype / settings"))
            { PanelOpen=!PanelOpen; draft=skill.Parameters; }
            if (!draft.IsValid && skill.Parameters.IsValid) draft=skill.Parameters;
            var state=skill.State;
            if (!PanelOpen)
            {
                if (!skill.Parameters.Enabled) return;
                GUI.Box(new Rect(Screen.width-300,Screen.height-144,290,96),
                    $"GLUTTONY   Passive: {Math.Max(0,state.PassiveReadyAt-NetworkTime.time):0.0}s\n"+
                    $"R Mark: {Math.Max(0,state.ActiveReadyAt-NetworkTime.time):0.0}s\n"+
                    $"Collected {state.Collected}/{state.Marked}   Remaining {skill.RemainingMarks}\n{skill.LastResult}");
                return;
            }
            GUILayout.BeginArea(new Rect(Screen.width-330,Mathf.Max(5,Screen.height-665),320,610),GUI.skin.box);
            GUILayout.Label("GLUTTONY PROTOTYPE - Host settings");
            bool oldEnabled=GUI.enabled; GUI.enabled=skill.isServer;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("OFF")) { draft.Enabled=false; ApplyDraft(false); }
            if (GUILayout.Button("Passive")) { draft.Enabled=true; draft.PassiveEnabled=true; draft.ActiveEnabled=false; ApplyDraft(false); }
            if (GUILayout.Button("Both")) { draft.Enabled=draft.PassiveEnabled=draft.ActiveEnabled=true; ApplyDraft(false); }
            GUILayout.EndHorizontal();
            draft.Enabled=GUILayout.Toggle(draft.Enabled,"Enable prototype");
            draft.PassiveEnabled=GUILayout.Toggle(draft.PassiveEnabled,"Passive devour");
            draft.ActiveEnabled=GUILayout.Toggle(draft.ActiveEnabled,"R rectangle mark");
            draft.PassiveCooldown=Slider("Passive cooldown",draft.PassiveCooldown,.1f,40);
            draft.ActiveCooldown=Slider("Active cooldown",draft.ActiveCooldown,.1f,40);
            draft.Radius=Slider("Devour radius",draft.Radius,.1f,5);
            draft.Length=Slider("Rectangle length",draft.Length,.2f,30);
            draft.Width=Slider("Rectangle width",draft.Width,.1f,15);
            draft.MaximumTargets=Mathf.RoundToInt(Slider("Maximum targets",draft.MaximumTargets,1,16));
            draft.MarkDuration=Slider("Mark duration",draft.MarkDuration,.2f,30);
            draft.FeedbackVolume=Slider("Prototype sound",draft.FeedbackVolume,0,1);
            if (GUILayout.Button("Apply parameters (clears active marks)")) ApplyDraft(false);
            if (GUILayout.Button("Reset cooldowns + clear marks (test)")) ApplyDraft(true);
            GUI.enabled=oldEnabled;
            GUILayout.Label(skill.isServer ? "R aims at mouse; movement/attacks stay active." : "Only the Host can change parameters.");
            GUILayout.Label($"Marked {state.Marked} / Ate {state.Collected} / Lost {state.Lost} / Expired {state.Expired}");
            GUILayout.EndArea();
        }
        private float Slider(string label, float value, float minimum, float maximum)
        {
            GUILayout.Label(label+": "+value.ToString("0.00"));
            return GUILayout.HorizontalSlider(value,minimum,maximum);
        }
        private void ApplyDraft(bool reset)
        {
            if (skill.isServer && draft.IsValid) NetworkCombatWorld.Instance.ServerConfigureGluttony(draft,reset);
        }
        private LineRenderer range;
        private void UpdateRange()
        {
            if (!skill.isOwned || !skill.Parameters.Enabled) { if (range!=null) range.enabled=false; return; }
            EnsureVisuals();
            if (range==null)
            {
                var go=new GameObject("Devour radius"); go.transform.SetParent(root.transform,false);
                range=go.AddComponent<LineRenderer>(); range.sharedMaterial=prototypeMaterial;
                range.loop=true; range.positionCount=48; range.widthMultiplier=.035f; range.sortingOrder=31990; range.sortingLayerID=SortingLayer.NameToID("Foreground");
            }
            range.enabled=true; Color color=NetworkTime.time>=skill.State.PassiveReadyAt ? new Color(.55f,1,.3f,.65f) : new Color(.75f,.75f,.75f,.3f);
            range.startColor=range.endColor=color;
            for(int i=0;i<48;i++) { float a=i*Mathf.PI*2/48; range.SetPosition(i,transform.position+new Vector3(Mathf.Cos(a),Mathf.Sin(a),0)*skill.Parameters.Radius); }
        }
    }
}
