using MonsterSupergroup.EditorTools;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NordicSample.Editor
{
    // Editor-only configuration. The saved map contains ordinary SpriteRenderers.
    public sealed class GameplayGrassArtProfile : ScriptableObject
    {
        [Header("地表：普通泥地 / 碎石泥地")]
        public Sprite soil;
        public Sprite rockySoil;
        [Header("整组草丛：level2，三款")]
        public Sprite[] clusters = new Sprite[3];
        [Header("独立小草：level3，三款")]
        public Sprite[] tufts = new Sprite[3];
        [Header("大小倍率：1 = 图片像素 ÷ PPU，再乘原布局缩放")]
        [Min(.01f)] public float floorScale = 1;
        [Min(.01f)] public float clusterScale = 1;
        [Min(.01f)] public float tuftScale = 1;
    }

    [CustomEditor(typeof(GameplayGrassArtProfile))]
    public sealed class GameplayGrassArtProfileInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("soil"), new GUIContent("普通土地 → level1_1"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rockySoil"), new GUIContent("碎石土地 → level1_2"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("clusters"), new GUIContent("整组草丛（level2）"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("tufts"), new GUIContent("独立小草（level3）"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("floorScale"), new GUIContent("地表大小倍率"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("clusterScale"), new GUIContent("草丛大小倍率"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("tuftScale"), new GUIContent("单株草大小倍率"));
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("草的切片锚点应为底部中央。覆盖同位置、同尺寸的 TGA 会自动更新；换 Sprite 或倍率后点击应用。只更新正式地图，不重建布局。", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("应用草地美术"))
                {
                    AssetDatabase.SaveAssetIfDirty(target);
                    ProjectToolRunner.Run("art.apply-grass", new ProjectToolRequest { apply = true });
                }
                if (GUILayout.Button("校验替换结果")) ProjectToolRunner.Run("validate.grass-art");
            }
        }
    }
}
