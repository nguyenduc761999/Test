using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Test Manager → Enemy Manager: manage EnemyConfig (add/remove, prefab + damage + health).
/// </summary>
public class EnemyManagerWindow : EditorWindow
{
    const string EnemyConfigAssetPath = "Assets/Test_Asset/Config/EnemyConfig.asset";
    const float ElementHeight = 80f;

    EnemyConfig _enemyConfig;
    ReorderableList _reorderableList;
    SerializedObject _serializedConfig;
    Vector2 _scroll;

    [MenuItem("Test Manager/Enemy Manager")]
    static void Open()
    {
        var window = GetWindow<EnemyManagerWindow>("Enemy Manager");
        window.minSize = new Vector2(480f, 360f);
        window.Show();
    }

    void OnEnable()
    {
        LoadOrCreateEnemyConfig();
        RebuildReorderableList();
    }

    void OnGUI()
    {
        EditorGUILayout.Space(8f);
        DrawConfigField();
        EditorGUILayout.Space(6f);

        if (_enemyConfig == null)
        {
            EditorGUILayout.HelpBox("No EnemyConfig assigned. Assign or create one above.", MessageType.Warning);
            return;
        }

        if (GUILayout.Button("Add Enemy", GUILayout.Height(28f)))
            AddEnemy();

        EditorGUILayout.Space(8f);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        _reorderableList?.DoLayoutList();
        EditorGUILayout.EndScrollView();

        if (_serializedConfig != null && _serializedConfig.ApplyModifiedProperties())
            EditorUtility.SetDirty(_enemyConfig);
    }

    void DrawConfigField()
    {
        EditorGUI.BeginChangeCheck();
        _enemyConfig = (EnemyConfig)EditorGUILayout.ObjectField(
            "Enemy Config",
            _enemyConfig,
            typeof(EnemyConfig),
            false);

        if (EditorGUI.EndChangeCheck())
            RebuildReorderableList();

        if (_enemyConfig == null && GUILayout.Button("Create EnemyConfig"))
        {
            LoadOrCreateEnemyConfig();
            RebuildReorderableList();
        }
    }

    void AddEnemy()
    {
        if (_enemyConfig == null)
            return;

        Undo.RecordObject(_enemyConfig, "Add Enemy");
        if (_enemyConfig.Enemies == null)
            return;

        _enemyConfig.Enemies.Add(new EnemyEntry());
        EditorUtility.SetDirty(_enemyConfig);
        RebuildReorderableList();
        Repaint();
    }

    void LoadOrCreateEnemyConfig()
    {
        _enemyConfig = AssetDatabase.LoadAssetAtPath<EnemyConfig>(EnemyConfigAssetPath);
        if (_enemyConfig != null)
            return;

        string folder = "Assets/Test_Asset/Config";
        if (!AssetDatabase.IsValidFolder(folder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Test_Asset"))
                AssetDatabase.CreateFolder("Assets", "Test_Asset");
            AssetDatabase.CreateFolder("Assets/Test_Asset", "Config");
        }

        _enemyConfig = CreateInstance<EnemyConfig>();
        AssetDatabase.CreateAsset(_enemyConfig, EnemyConfigAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    void RebuildReorderableList()
    {
        _reorderableList = null;
        _serializedConfig = null;

        if (_enemyConfig == null)
            return;

        _serializedConfig = new SerializedObject(_enemyConfig);
        SerializedProperty listProp = _serializedConfig.FindProperty("_enemies");
        if (listProp == null)
            return;

        _reorderableList = new ReorderableList(_serializedConfig, listProp, true, true, false, false);
        _reorderableList.drawHeaderCallback = rect =>
        {
            EditorGUI.LabelField(rect, $"Enemies  ({listProp.arraySize})");
        };

        _reorderableList.elementHeight = ElementHeight;
        _reorderableList.drawElementCallback = (rect, index, active, focused) =>
        {
            DrawEnemyElement(rect, listProp, index);
        };
    }

    void DrawEnemyElement(Rect rect, SerializedProperty listProp, int index)
    {
        if (index < 0 || index >= listProp.arraySize)
            return;

        rect.y += 2f;
        float line = EditorGUIUtility.singleLineHeight;
        float gap = 4f;
        float labelW = 80f;
        float buttonW = 64f;

        SerializedProperty element = listProp.GetArrayElementAtIndex(index);
        SerializedProperty prefabProp = element.FindPropertyRelative("_prefab");
        SerializedProperty damageProp = element.FindPropertyRelative("_damage");
        SerializedProperty healthProp = element.FindPropertyRelative("_health");

        float y = rect.y;

        // Row 1: Prefab + Delete
        DrawLabeledObject(rect.x, y, rect.width - buttonW - gap, labelW, "Prefab", prefabProp, typeof(GameObject));
        Rect deleteRect = new Rect(rect.x + rect.width - buttonW, y, buttonW, line);
        if (GUI.Button(deleteRect, "Delete"))
            TryDeleteEnemy(listProp, index);
        y += line + gap;

        // Row 2: Damage
        Rect dmgLabelRect = new Rect(rect.x, y, labelW, line);
        Rect dmgFieldRect = new Rect(dmgLabelRect.xMax + gap, y, rect.width - labelW - gap, line);
        EditorGUI.LabelField(dmgLabelRect, "Damage");
        damageProp.floatValue = Mathf.Max(0f, EditorGUI.FloatField(dmgFieldRect, damageProp.floatValue));
        y += line + gap;

        // Row 3: Health
        Rect hpLabelRect = new Rect(rect.x, y, labelW, line);
        Rect hpFieldRect = new Rect(hpLabelRect.xMax + gap, y, rect.width - labelW - gap, line);
        EditorGUI.LabelField(hpLabelRect, "Health");
        healthProp.floatValue = Mathf.Max(0f, EditorGUI.FloatField(hpFieldRect, healthProp.floatValue));
    }

    static void DrawLabeledObject(
        float x,
        float y,
        float totalWidth,
        float labelW,
        string label,
        SerializedProperty prop,
        System.Type type)
    {
        float gap = 4f;
        float line = EditorGUIUtility.singleLineHeight;
        Rect labelRect = new Rect(x, y, labelW, line);
        Rect fieldRect = new Rect(labelRect.xMax + gap, y, totalWidth - labelW - gap, line);
        EditorGUI.LabelField(labelRect, label);
        prop.objectReferenceValue = EditorGUI.ObjectField(fieldRect, prop.objectReferenceValue, type, false);
    }

    void TryDeleteEnemy(SerializedProperty listProp, int index)
    {
        SerializedProperty element = listProp.GetArrayElementAtIndex(index);
        SerializedProperty prefabProp = element.FindPropertyRelative("_prefab");
        string displayName = prefabProp.objectReferenceValue != null
            ? prefabProp.objectReferenceValue.name
            : $"(Enemy {index})";

        bool confirmed = EditorUtility.DisplayDialog(
            "Delete Enemy",
            $"Delete \"{displayName}\" from EnemyConfig?",
            "Delete",
            "Cancel");

        if (!confirmed)
            return;

        listProp.DeleteArrayElementAtIndex(index);
        _serializedConfig.ApplyModifiedProperties();
        EditorUtility.SetDirty(_enemyConfig);
        RebuildReorderableList();
        Repaint();
    }
}
