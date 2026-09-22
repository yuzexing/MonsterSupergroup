using System;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;
namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class GluttonyPrototypeView
    {
        public bool PanelOpen { get; private set; }
        private GluttonyParameters draft;
        private MusicParameters musicDraft;
        private AllureParameters allureDraft;
        private int parameterTab;
        private Vector2 parameterScroll;
        private void OnGUI()
        {
            if (skill==null || !skill.isOwned || !skill.isClient) return;
            var abilities=GetComponent<NetworkPlayerPrototypeAbilities>();
            if (abilities==null) return;
            var music=GetComponent<NetworkPlayerMusic>();
            var musicView=GetComponent<MusicPrototypeView>();
            var allure=GetComponent<NetworkPlayerAllure>();
            if (GUI.Button(new Rect(Screen.width-350,Screen.height-44,340,34),PanelOpen ? "Close prototype settings" : "Prototype abilities / settings"))
            { PanelOpen=!PanelOpen; draft=skill.Parameters; if (music!=null) musicDraft=music.Parameters; if (allure!=null) allureDraft=allure.Parameters; }
            if (!draft.IsValid && skill.Parameters.IsValid) draft=skill.Parameters;
            if (!musicDraft.IsValid && music!=null && music.Parameters.IsValid) musicDraft=music.Parameters;
            if (!allureDraft.IsValid && allure!=null && allure.Parameters.IsValid) allureDraft=allure.Parameters;
            var state=skill.State;
            if (!PanelOpen)
            {
                string title=abilities.PrototypeEnabled ? NetworkPlayerPrototypeAbilities.DisplayName(abilities.SelectedAbility) : "PROTOTYPES OFF";
                string status=abilities.PrototypeEnabled ? abilities.SelectedStatus : "Host can enable prototypes in settings.";
                string marks=skill.RemainingMarks>0 && !skill.IsSelected ? $"\nGluttony: {skill.RemainingMarks} marks still collectable" : "";
                string ongoing=music!=null && abilities.SelectedAbility!=PrototypeAbilityId.Music ?
                    (music.IsPerforming ? "\n"+(musicView!=null && musicView.IsPresenting ? musicView.PerformanceStatus : music.StatusText) :
                    music.SpeedRemaining>0 ? $"\nMusic speed: {music.SpeedRemaining:0.0}s" : "") : "";
                if (allure!=null && abilities.SelectedAbility!=PrototypeAbilityId.Allure && allure.DecoyRemaining>0)
                    ongoing+=$"\nAllure decoy: {allure.DecoyRemaining:0.0}s";
                string last=skill.IsSelected ? skill.LastResult :
                    (abilities.SelectedAbility==PrototypeAbilityId.Music && music!=null) ||
                    (abilities.SelectedAbility==PrototypeAbilityId.Allure && allure!=null) ? "" : abilities.LastResult;
                GUI.Box(new Rect(Screen.width-350,Screen.height-278,340,228),
                    $"{title}{(abilities.SelectionPending ? " (switching...)" : "")}\n"+
                    "1 Gluttony | 2 Music | 3 Allure\n"+
                    "R Primary | T Transfer | F Decoy | Space Rhythm\n"+
                    status+marks+ongoing+"\n"+last);
                return;
            }
            float panelHeight=Mathf.Min(670,Screen.height-65);
            GUILayout.BeginArea(new Rect(Screen.width-350,Screen.height-55-panelHeight,340,panelHeight),GUI.skin.box);
            GUILayout.Label("PROTOTYPE ABILITIES - Host session settings");
            bool oldEnabled=GUI.enabled; GUI.enabled=skill.isServer;
            bool enabled=GUILayout.Toggle(abilities.PrototypeEnabled,"Enable prototype abilities for this session");
            if (enabled!=abilities.PrototypeEnabled) NetworkCombatWorld.Instance.ServerConfigurePrototypesEnabled(enabled);
            GUI.enabled=oldEnabled;
            if (musicView!=null)
            {
                GUI.enabled=!musicView.IsPresenting;
                float calibration=Slider("Local input offset (ms)",musicView.InputCalibrationSeconds*1000,-200,200);
                musicView.InputCalibrationSeconds=calibration/1000;
                GUI.enabled=oldEnabled;
                GUILayout.Label("Positive offset compensates for late input.");
            }
            parameterTab=GUILayout.Toolbar(parameterTab,new[] { "Gluttony", "Music", "Allure" });
            parameterScroll=GUILayout.BeginScrollView(parameterScroll);
            GUI.enabled=skill.isServer;
            if (parameterTab==0) DrawGluttonyParameters();
            else if (parameterTab==1) DrawMusicParameters(music);
            else DrawAllureParameters(allure);
            GUI.enabled=oldEnabled;
            GUILayout.EndScrollView();
            GUILayout.Label(skill.isServer ? "Session changes are not saved to assets." : "Only the Host can change session parameters.");
            GUILayout.Label($"Gluttony: {state.Marked} marked / {state.Collected} ate / {state.Lost} lost / {state.Expired} expired");
            GUILayout.EndArea();
        }
        private void DrawGluttonyParameters()
        {
            GUILayout.Label("GLUTTONY PARAMETERS");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Gluttony OFF")) { draft.Enabled=false; ApplyDraft(false); }
            if (GUILayout.Button("Passive")) { draft.Enabled=true; draft.PassiveEnabled=true; draft.ActiveEnabled=false; ApplyDraft(false); }
            if (GUILayout.Button("Both")) { draft.Enabled=draft.PassiveEnabled=draft.ActiveEnabled=true; ApplyDraft(false); }
            GUILayout.EndHorizontal();
            draft.Enabled=GUILayout.Toggle(draft.Enabled,"Enable Gluttony module");
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
        }
        private void DrawMusicParameters(NetworkPlayerMusic music)
        {
            if (music==null) { GUILayout.Label("Music component is not installed on this player."); return; }
            GUILayout.Label("MUSIC - R start / Space rhythm / fixed 10 beats");
            musicDraft.Bpm=Slider("BPM",musicDraft.Bpm,60,240);
            musicDraft.CountInBeats=Mathf.RoundToInt(Slider("Count-in beats",musicDraft.CountInBeats,1,4));
            musicDraft.HitWindowSeconds=Slider("Hit window (seconds)",musicDraft.HitWindowSeconds,.03f,Mathf.Min(.2f,30/musicDraft.Bpm-.001f));
            musicDraft.Cooldown=Slider("Cooldown after end",musicDraft.Cooldown,.1f,120);
            musicDraft.FeedbackVolume=Slider("Rhythm cue volume",musicDraft.FeedbackVolume,0,1);
            musicDraft.PushRadius=Slider("Push radius",musicDraft.PushRadius,.1f,20);
            musicDraft.PushDistance=Slider("Push distance",musicDraft.PushDistance,.1f,10);
            musicDraft.PushDuration=Slider("Push duration",musicDraft.PushDuration,.05f,2);
            musicDraft.LightningRadius=Slider("Lightning radius",musicDraft.LightningRadius,.1f,20);
            musicDraft.LightningTargets=Mathf.RoundToInt(Slider("Lightning targets",musicDraft.LightningTargets,1,32));
            musicDraft.LightningDamage=Slider("Lightning damage",musicDraft.LightningDamage,0,200);
            musicDraft.SpeedBonus=Slider("Speed bonus",musicDraft.SpeedBonus,0,2);
            musicDraft.SpeedDuration=Slider("Speed duration",musicDraft.SpeedDuration,.1f,20);
            musicDraft.FinaleRadius=Slider("Perfect finale radius",musicDraft.FinaleRadius,.1f,20);
            musicDraft.FinaleDamage=Slider("Perfect finale damage",musicDraft.FinaleDamage,0,500);
            if (GUILayout.Button("Apply music (ends current effects)")) ApplyMusicDraft(false);
            if (GUILayout.Button("Reset music cooldown + effects (test)")) ApplyMusicDraft(true);
            GUILayout.Label("Switching retains rhythm input and active speed.");
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
        private void ApplyMusicDraft(bool reset)
        {
            if (skill.isServer && musicDraft.IsValid) NetworkCombatWorld.Instance.ServerConfigureMusic(musicDraft,reset);
        }
        private void DrawAllureParameters(NetworkPlayerAllure allure)
        {
            if (allure==null) { GUILayout.Label("Allure component is not installed on this player."); return; }
            GUILayout.Label("ALLURE - R throw / T take / F decoy");
            allureDraft.ThrowCooldown=Slider("Throw cooldown",allureDraft.ThrowCooldown,.1f,120);
            allureDraft.TakeCooldown=Slider("Take cooldown",allureDraft.TakeCooldown,.1f,120);
            allureDraft.DecoyCooldown=Slider("Decoy cooldown",allureDraft.DecoyCooldown,.1f,120);
            allureDraft.DecoyDuration=Slider("Decoy duration",allureDraft.DecoyDuration,.1f,30);
            allureDraft.MaximumTargets=Mathf.RoundToInt(Slider("Maximum targets",allureDraft.MaximumTargets,1,32));
            if (GUILayout.Button("Apply allure (clears current decoys)")) ApplyAllureDraft(false);
            if (GUILayout.Button("Reset allure cooldowns + decoys (test)")) ApplyAllureDraft(true);
            GUILayout.Label("R/T need exactly two living players. F also works solo.");
            GUILayout.Label("Source screen and current aggro determine eligible enemies.");
            GUILayout.Label("Switching keeps cooldowns and active decoys.");
        }
        private void ApplyAllureDraft(bool reset)
        {
            if (skill.isServer && allureDraft.IsValid) NetworkCombatWorld.Instance.ServerConfigureAllure(allureDraft,reset);
        }
        private LineRenderer range;
        private void UpdateRange()
        {
            if (!skill.isOwned || !skill.Parameters.Enabled || !skill.IsPrototypeEnabled ||
                (!skill.IsSelected && skill.RemainingMarks==0)) { if (range!=null) range.enabled=false; return; }
            EnsureVisuals();
            if (range==null)
            {
                var go=new GameObject("Devour radius"); go.transform.SetParent(root.transform,false);
                range=go.AddComponent<LineRenderer>(); range.sharedMaterial=prototypeMaterial;
                range.loop=true; range.positionCount=48; range.widthMultiplier=.035f; range.sortingOrder=31990; range.sortingLayerID=SortingLayer.NameToID("Foreground");
            }
            range.enabled=true; Color color=skill.IsSelected && skill.Parameters.PassiveEnabled && NetworkTime.time>=skill.State.PassiveReadyAt ? new Color(.55f,1,.3f,.65f) : new Color(.75f,.75f,.75f,.3f);
            range.startColor=range.endColor=color;
            for(int i=0;i<48;i++) { float a=i*Mathf.PI*2/48; range.SetPosition(i,transform.position+new Vector3(Mathf.Cos(a),Mathf.Sin(a),0)*skill.Parameters.Radius); }
        }
    }
}
