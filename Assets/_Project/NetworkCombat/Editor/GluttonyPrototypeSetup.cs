using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace MonsterSupergroup.NetworkCombat.Editor
{
    [InitializeOnLoad]
    public static class GluttonyPrototypeSetup
    {
        private const string Folder="Assets/_Project/Content/NetworkCombat/";
        private const string Boot="Assets/_Project/Scenes/Boot.unity";
        private const string Log="Logs/GluttonyPrototype/";
        private static double nextPoll;
        static GluttonyPrototypeSetup() { EditorApplication.update+=Poll; }
        [MenuItem("Tools/MonsterSupergroup/Prototypes/Gluttony/Install or Refresh")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before installing.");
            var scene=SceneManager.GetSceneByPath(Boot); bool opened=!scene.IsValid();
            if (!opened && scene.isDirty) throw new InvalidOperationException("Boot has unsaved edits; not overwriting them.");
            var prefabStage=PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage!=null && prefabStage.scene.isDirty) throw new InvalidOperationException("Prefab stage has unsaved edits.");
            Directory.CreateDirectory(Log+"BeforeSetup");
            Backup(Boot); Backup(Folder+"NetworkPlayer.prefab");
            var config=AssetDatabase.LoadAssetAtPath<GluttonyPrototypeConfig>(Folder+"GluttonyPrototype.asset");
            if (config==null) { config=ScriptableObject.CreateInstance<GluttonyPrototypeConfig>(); AssetDatabase.CreateAsset(config,Folder+"GluttonyPrototype.asset"); }
            var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+"GluttonyPrototype.mat");
            if (material==null)
            {
                var shader=Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
                if (shader==null) throw new InvalidOperationException("A sprite shader is required.");
                material=new Material(shader); AssetDatabase.CreateAsset(material,Folder+"GluttonyPrototype.mat");
            }
            var root=PrefabUtility.LoadPrefabContents(Folder+"NetworkPlayer.prefab");
            try
            {
                var skill=root.GetComponent<NetworkPlayerGluttony>() ?? root.AddComponent<NetworkPlayerGluttony>();
                SetObject(skill,"config",config); SetObject(root.GetComponent<GluttonyPrototypeView>(),"prototypeMaterial",material);
                PrefabUtility.SaveAsPrefabAsset(root,Folder+"NetworkPlayer.prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            if (opened) scene=EditorSceneManager.OpenScene(Boot,OpenSceneMode.Additive);
            try
            {
                var input=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MonoBehaviour>(true))
                    .First(c=>c!=null && c.GetType().FullName=="Rewired.InputManager");
                ConfigureInput(new SerializedObject(input));
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene,true); }
            File.WriteAllText(Log+"installed.txt",DateTime.UtcNow.ToString("O")+"\nDefault OFF. Open Gluttony panel in-game and select Both.");
        }
        private static void ConfigureInput(SerializedObject so)
        {
            var actions=so.FindProperty("_userData.actions"); var action=Find(actions,"_id",5);
            if (action==null) { int index=actions.arraySize++; action=actions.GetArrayElementAtIndex(index); }
            else if (action.FindPropertyRelative("_name").stringValue!="Button3") throw new InvalidOperationException("Action 5 is already assigned.");
            Int(action,"_id",5); Int(action,"_type",1); Int(action,"_categoryId",1); Int(action,"_behaviorId",0); Int(action,"_userAssignable",1);
            action.FindPropertyRelative("_name").stringValue="Button3";
            action.FindPropertyRelative("_descriptiveName").stringValue="Gluttony Mark (Prototype)";
            var ids=Find(so.FindProperty("_userData.actionCategoryMap.list"),"categoryId",1).FindPropertyRelative("actionIds");
            bool exists=false; for(int i=0;i<ids.arraySize;i++) exists|=ids.GetArrayElementAtIndex(i).intValue==5;
            if (!exists) { int index=ids.arraySize++; ids.GetArrayElementAtIndex(index).intValue=5; }
            var map=Find(so.FindProperty("_userData.keyboardMaps"),"categoryId",1).FindPropertyRelative("actionElementMaps");
            var key=Find(map,"_keyboardKeyCode",114);
            if (key!=null && key.FindPropertyRelative("_actionId").intValue!=5) throw new InvalidOperationException("R is already assigned.");
            if (key==null) { int index=map.arraySize++; key=map.GetArrayElementAtIndex(index); }
            Int(key,"_actionCategoryId",1); Int(key,"_actionId",5); Int(key,"_elementType",1); Int(key,"_elementIdentifierId",-1);
            Int(key,"_axisRange",0); Int(key,"_invert",0); Int(key,"_axisContribution",0); Int(key,"_keyboardKeyCode",114);
            Int(key,"_modifierKey1",0); Int(key,"_modifierKey2",0); Int(key,"_modifierKey3",0);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static SerializedProperty Find(SerializedProperty list,string field,int id)
        {
            for(int i=0;i<list.arraySize;i++)
            {
                var item=list.GetArrayElementAtIndex(i);
                if (item.FindPropertyRelative(field).intValue==id) return item;
            }
            return null;
        }
        private static void Int(SerializedProperty item,string name,int value)
        {
            var property=item.FindPropertyRelative(name);
            if (property.propertyType==SerializedPropertyType.Boolean) property.boolValue=value!=0;
            else property.intValue=value;
        }
        private static void SetObject(UnityEngine.Object target,string field,UnityEngine.Object value)
        {
            var so=new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue=value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void Backup(string path)
        {
            string destination=Log+"BeforeSetup/"+Path.GetFileName(path);
            if (!File.Exists(destination)) File.Copy(path,destination);
        }
        private static string Status()
        {
            string text=$"Unity={Application.unityVersion} playing={EditorApplication.isPlaying} compiling={EditorApplication.isCompiling}\n";
            for(int i=0;i<SceneManager.sceneCount;i++)
            { var scene=SceneManager.GetSceneAt(i); text+=$"{scene.path} dirty={scene.isDirty}\n"; }
            return text;
        }
        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup<nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll=EditorApplication.timeSinceStartup+.5;
            string request=Log+"editor-command.txt";
            if (!File.Exists(request)) return;
            string command=File.ReadAllText(request).Trim(); File.Delete(request);
            try
            {
                switch(command)
                {
                    case "status": break;
                    case "install": Install(); break;
                    case "refresh": AssetDatabase.Refresh(); break;
                    case "edit-tests":
                        if (!EditorApplication.ExecuteMenuItem("Tools/MonsterSupergroup/Prototypes/Gluttony/Run EditMode Tests"))
                            throw new InvalidOperationException("EditMode test menu is not loaded.");
                        break;
                    case "play-tests":
                        if (!EditorApplication.ExecuteMenuItem("Tools/MonsterSupergroup/Prototypes/Gluttony/Run PlayMode Tests"))
                            throw new InvalidOperationException("PlayMode test menu is not loaded.");
                        break;
                    default: throw new ArgumentException("Unknown Gluttony editor command.");
                }
                File.WriteAllText(Log+"editor-result.txt",command+" OK\n"+Status());
            }
            catch(Exception error)
            {
                File.WriteAllText(Log+"editor-result.txt",command+" FAILED\n"+error);
                Debug.LogException(error);
            }
        }
    }
}
