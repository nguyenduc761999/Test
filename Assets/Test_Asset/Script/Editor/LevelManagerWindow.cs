using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Test Manager → Level Manager: quản lý LevelConfig (thêm/xóa, timeSpawn + list zombie prefab).
/// </summary>
public class LevelManagerWindow : EditorWindow
{
    const string LevelConfigAssetPath = "Assets/Test_Asset/Config/LevelConfig.asset";
    const float BaseElementHeight = 76f;
    const float PrefabRowHeight = 20f;

    LevelConfig _levelConfig;
    ReorderableList _reorderableList;
    SerializedObject _serializedConfig;
    Vector2 _scroll;

    [MenuItem("Test Manager/Level Manager")]
    static void Open()
    {
        var window = GetWindow<LevelManagerWindow>("Level Manager");
        window.minSize = new Vector2(520f, 400f);
        window.Show();
    }

    void OnEnable()
    {
        LoadOrCreateLevelConfig();
        RebuildReorderableList();
    }

    void OnGUI()
    {
        EditorGUILayout.Space(8f);
        DrawConfigField();
        EditorGUILayout.Space(6f);

        if (_levelConfig == null)
        {
            EditorGUILayout.HelpBox("Chưa gán LevelConfig. Gán hoặc tạo mới phía trên.", MessageType.Warning);
            return;
        }

        if (GUILayout.Button("Add Level", GUILayout.Height(28f)))
            AddLevel();

        EditorGUILayout.Space(8f);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        _reorderableList?.DoLayoutList();
        EditorGUILayout.EndScrollView();

        if (_serializedConfig != null && _serializedConfig.ApplyModifiedProperties())
            EditorUtility.SetDirty(_levelConfig);
    }

    void DrawConfigField()
    {
        EditorGUI.BeginChangeCheck();
        _levelConfig = (LevelConfig)EditorGUILayout.ObjectField(
            "Level Config",
            _levelConfig,
            typeof(LevelConfig),
            false);

        if (EditorGUI.EndChangeCheck())
            RebuildReorderableList();

        if (_levelConfig == null && GUILayout.Button("Create LevelConfig"))
        {
            LoadOrCreateLevelConfig();
            RebuildReorderableList();
        }
    }

    void AddLevel()
    {
        if (_levelConfig == null)
            return;

        Undo.RecordObject(_levelConfig, "Add Level");
        if (_levelConfig.Levels == null)
            return;

        _levelConfig.Levels.Add(new LevelEntry());
        EditorUtility.SetDirty(_levelConfig);
        RebuildReorderableList();
        Repaint();
    }

    void LoadOrCreateLevelConfig()
    {
        _levelConfig = AssetDatabase.LoadAssetAtPath<LevelConfig>(LevelConfigAssetPath);
        if (_levelConfig != null)
            return;

        string folder = "Assets/Test_Asset/Config";
        if (!AssetDatabase.IsValidFolder(folder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Test_Asset"))
                AssetDatabase.CreateFolder("Assets", "Test_Asset");
            AssetDatabase.CreateFolder("Assets/Test_Asset", "Config");
        }

        _levelConfig = CreateInstance<LevelConfig>();
        AssetDatabase.CreateAsset(_levelConfig, LevelConfigAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    void RebuildReorderableList()
    {
        _reorderableList = null;
        _serializedConfig = null;

        if (_levelConfig == null)
            return;

        _serializedConfig = new SerializedObject(_levelConfig);
        SerializedProperty listProp = _serializedConfig.FindProperty("_levels");
        if (listProp == null)
            return;

        _reorderableList = new ReorderableList(_serializedConfig, listProp, true, true, false, false);
        _reorderableList.drawHeaderCallback = rect =>
        {
            EditorGUI.LabelField(rect, $"Levels  ({listProp.arraySize})");
        };

        _reorderableList.elementHeightCallback = index =>
        {
            if (index < 0 || index >= listProp.arraySize)
                return BaseElementHeight;

            SerializedProperty element = listProp.GetArrayElementAtIndex(index);
            SerializedProperty prefabsProp = element.FindPropertyRelative("_zombiePrefabs");
            int prefabCount = prefabsProp != null ? prefabsProp.arraySize : 0;
            // time + header list + rows + nút add + padding
            return BaseElementHeight + PrefabRowHeight * (prefabCount + 2) + 28f;
        };

        _reorderableList.drawElementCallback = (rect, index, active, focused) =>
        {
            DrawLevelElement(rect, listProp, index);
        };
    }

    void DrawLevelElement(Rect rect, SerializedProperty listProp, int index)
    {
        if (index < 0 || index >= listProp.arraySize)
            return;

        rect.y += 2f;
        float line = EditorGUIUtility.singleLineHeight;
        float gap = 4f;
        float labelW = 120f;
        float buttonW = 64f;

        SerializedProperty element = listProp.GetArrayElementAtIndex(index);
        SerializedProperty timeProp = element.FindPropertyRelative("_timeSpawnZombie");
        SerializedProperty playTimeProp = element.FindPropertyRelative("_playTime");
        SerializedProperty prefabsProp = element.FindPropertyRelative("_zombiePrefabs");

        float y = rect.y;

        // Hàng 1: Level index + Delete
        Rect titleRect = new Rect(rect.x, y, rect.width - buttonW - gap, line);
        EditorGUI.LabelField(titleRect, $"Level {index}", EditorStyles.boldLabel);
        Rect deleteRect = new Rect(rect.x + rect.width - buttonW, y, buttonW, line);
        if (GUI.Button(deleteRect, "Delete"))
            TryDeleteLevel(listProp, index);
        y += line + gap;

        // Hàng 2: Time Spawn Zombie
        Rect timeLabelRect = new Rect(rect.x, y, labelW, line);
        Rect timeFieldRect = new Rect(timeLabelRect.xMax + gap, y, rect.width - labelW - gap, line);
        EditorGUI.LabelField(timeLabelRect, "Time Spawn");
        timeProp.floatValue = Mathf.Max(0f, EditorGUI.FloatField(timeFieldRect, timeProp.floatValue));
        y += line + gap;

        // Hàng 3: Play Time (giây)
        Rect playLabelRect = new Rect(rect.x, y, labelW, line);
        Rect playFieldRect = new Rect(playLabelRect.xMax + gap, y, rect.width - labelW - gap, line);
        EditorGUI.LabelField(playLabelRect, "Play Time (s)");
        if (playTimeProp != null)
            playTimeProp.floatValue = Mathf.Max(0f, EditorGUI.FloatField(playFieldRect, playTimeProp.floatValue));
        y += line + gap;

        // Hàng 4+: List zombie prefab
        EditorGUI.LabelField(new Rect(rect.x, y, rect.width, line), "Zombie Prefabs");
        y += line + 2f;

        if (prefabsProp != null)
        {
            for (int i = 0; i < prefabsProp.arraySize; i++)
            {
                SerializedProperty prefabProp = prefabsProp.GetArrayElementAtIndex(i);
                float rowButtonW = 56f;
                Rect fieldRect = new Rect(rect.x, y, rect.width - rowButtonW - gap, line);
                Rect removeRect = new Rect(rect.x + rect.width - rowButtonW, y, rowButtonW, line);

                prefabProp.objectReferenceValue = EditorGUI.ObjectField(
                    fieldRect,
                    prefabProp.objectReferenceValue,
                    typeof(GameObject),
                    false);

                if (GUI.Button(removeRect, "X"))
                {
                    prefabsProp.DeleteArrayElementAtIndex(i);
                    break;
                }

                y += PrefabRowHeight;
            }

            Rect addRect = new Rect(rect.x, y, 120f, line);
            if (GUI.Button(addRect, "Add Prefab"))
                prefabsProp.arraySize++;
        }
    }

    void TryDeleteLevel(SerializedProperty listProp, int index)
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "Delete Level",
            $"Xóa Level {index} khỏi LevelConfig?",
            "Delete",
            "Cancel");

        if (!confirmed)
            return;

        listProp.DeleteArrayElementAtIndex(index);
        _serializedConfig.ApplyModifiedProperties();
        EditorUtility.SetDirty(_levelConfig);
        RebuildReorderableList();
        Repaint();
    }
}
