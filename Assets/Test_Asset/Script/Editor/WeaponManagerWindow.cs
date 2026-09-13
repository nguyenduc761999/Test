using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Test Manager → Weapon Manager: manage WeaponConfig (add/remove, Add to Inventory).
/// </summary>
public class WeaponManagerWindow : EditorWindow
{
    const string WeaponConfigAssetPath = "Assets/Test_Asset/Config/WeaponConfig.asset";
    const string SoundConfigAssetPath = "Assets/Test_Asset/Config/SoundConfig.asset";
    const string UserConfigAssetPath = "Assets/Test_Asset/Config/UserConfig.asset";
    const float IconPreviewSize = 72f;
    const float ElementHeight = 276f;

    WeaponConfig _weaponConfig;
    SoundConfig _soundConfig;
    UserConfig _userConfig;
    ReorderableList _reorderableList;
    SerializedObject _serializedConfig;
    Vector2 _scroll;

    [MenuItem("Test Manager/Weapon Manager")]
    static void Open()
    {
        var window = GetWindow<WeaponManagerWindow>("Weapon Manager");
        window.minSize = new Vector2(560f, 420f);
        window.Show();
    }

    void OnEnable()
    {
        LoadOrCreateWeaponConfig();
        _soundConfig = AssetDatabase.LoadAssetAtPath<SoundConfig>(SoundConfigAssetPath);
        _userConfig = AssetDatabase.LoadAssetAtPath<UserConfig>(UserConfigAssetPath);
        RebuildReorderableList();
    }

    void OnGUI()
    {
        EditorGUILayout.Space(8f);
        DrawConfigField();
        EditorGUILayout.Space(6f);

        if (_weaponConfig == null)
        {
            EditorGUILayout.HelpBox("No WeaponConfig assigned. Assign or create one above.", MessageType.Warning);
            return;
        }

        if (GUILayout.Button("Add Weapon", GUILayout.Height(28f)))
            AddWeapon();

        EditorGUILayout.Space(8f);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        _reorderableList?.DoLayoutList();
        EditorGUILayout.EndScrollView();

        if (_serializedConfig != null && _serializedConfig.ApplyModifiedProperties())
            EditorUtility.SetDirty(_weaponConfig);
    }

    void DrawConfigField()
    {
        EditorGUI.BeginChangeCheck();
        _weaponConfig = (WeaponConfig)EditorGUILayout.ObjectField(
            "Weapon Config",
            _weaponConfig,
            typeof(WeaponConfig),
            false);

        if (EditorGUI.EndChangeCheck())
            RebuildReorderableList();

        if (_weaponConfig == null && GUILayout.Button("Create WeaponConfig"))
        {
            LoadOrCreateWeaponConfig();
            RebuildReorderableList();
        }

        EditorGUI.BeginChangeCheck();
        _soundConfig = (SoundConfig)EditorGUILayout.ObjectField(
            "Sound Config (SFX)",
            _soundConfig,
            typeof(SoundConfig),
            false);
        _userConfig = (UserConfig)EditorGUILayout.ObjectField(
            "User Config",
            _userConfig,
            typeof(UserConfig),
            false);
        EditorGUI.EndChangeCheck();
    }

    void AddWeapon()
    {
        if (_weaponConfig == null)
            return;

        Undo.RecordObject(_weaponConfig, "Add Weapon");
        if (_weaponConfig.Weapons == null)
            return;

        _weaponConfig.Weapons.Add(new WeaponEntry());
        EditorUtility.SetDirty(_weaponConfig);
        RebuildReorderableList();
        Repaint();
    }

    void LoadOrCreateWeaponConfig()
    {
        _weaponConfig = AssetDatabase.LoadAssetAtPath<WeaponConfig>(WeaponConfigAssetPath);
        if (_weaponConfig != null)
            return;

        string folder = "Assets/Test_Asset/Config";
        if (!AssetDatabase.IsValidFolder(folder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Test_Asset"))
                AssetDatabase.CreateFolder("Assets", "Test_Asset");
            AssetDatabase.CreateFolder("Assets/Test_Asset", "Config");
        }

        _weaponConfig = CreateInstance<WeaponConfig>();
        AssetDatabase.CreateAsset(_weaponConfig, WeaponConfigAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    void RebuildReorderableList()
    {
        _reorderableList = null;
        _serializedConfig = null;

        if (_weaponConfig == null)
            return;

        _serializedConfig = new SerializedObject(_weaponConfig);
        SerializedProperty listProp = _serializedConfig.FindProperty("_weapons");
        if (listProp == null)
            return;

        _reorderableList = new ReorderableList(_serializedConfig, listProp, true, true, false, false);
        _reorderableList.drawHeaderCallback = rect =>
        {
            EditorGUI.LabelField(rect, $"Weapons  ({listProp.arraySize})");
        };

        _reorderableList.elementHeight = ElementHeight;
        _reorderableList.drawElementCallback = (rect, index, active, focused) =>
        {
            DrawWeaponElement(rect, listProp, index);
        };
    }

    void DrawWeaponElement(Rect rect, SerializedProperty listProp, int index)
    {
        if (index < 0 || index >= listProp.arraySize)
            return;

        rect.y += 2f;
        float line = EditorGUIUtility.singleLineHeight;
        float gap = 4f;
        float labelW = 80f;
        float buttonW = 64f;

        SerializedProperty element = listProp.GetArrayElementAtIndex(index);
        SerializedProperty iconProp = element.FindPropertyRelative("_icon");
        SerializedProperty gunProp = element.FindPropertyRelative("_gunPrefab");
        SerializedProperty bulletProp = element.FindPropertyRelative("_bulletPrefab");
        SerializedProperty fireVfxProp = element.FindPropertyRelative("_fireVfx");
        SerializedProperty soundProp = element.FindPropertyRelative("_sound");
        SerializedProperty damageProp = element.FindPropertyRelative("_damage");
        SerializedProperty bulletSpeedProp = element.FindPropertyRelative("_bulletSpeed");
        SerializedProperty fireSpeedProp = element.FindPropertyRelative("_fireSpeed");

        float y = rect.y;

        // Row 0: large icon preview + ObjectField + Delete
        DrawIconRow(rect.x, y, rect.width, labelW, buttonW, gap, line, iconProp, () => TryDeleteWeapon(listProp, index));
        y += IconPreviewSize + gap;

        // Row 1: Gun
        DrawLabeledObject(rect.x, y, rect.width, labelW, "Prefab Gun", gunProp, typeof(GameObject));
        y += line + gap;

        // Row 2: Bullet
        DrawLabeledObject(rect.x, y, rect.width, labelW, "Prefab Bullet", bulletProp, typeof(GameObject));
        y += line + gap;

        // Row 3: Fire VFX
        DrawLabeledObject(rect.x, y, rect.width, labelW, "Fire VFX", fireVfxProp, typeof(GameObject));
        y += line + gap;

        // Row 4: Sound dropdown from SFX Sound Manager
        Rect soundLabelRect = new Rect(rect.x, y, labelW, line);
        Rect soundFieldRect = new Rect(soundLabelRect.xMax + gap, y, rect.width - labelW - gap, line);
        EditorGUI.LabelField(soundLabelRect, "Sound");
        DrawSfxDropdown(soundFieldRect, soundProp);
        y += line + gap;

        // Row 5: Bullet Speed
        Rect speedLabelRect = new Rect(rect.x, y, labelW, line);
        Rect speedFieldRect = new Rect(speedLabelRect.xMax + gap, y, rect.width - labelW - gap, line);
        EditorGUI.LabelField(speedLabelRect, "Bullet Speed");
        bulletSpeedProp.floatValue = Mathf.Max(0f, EditorGUI.FloatField(speedFieldRect, bulletSpeedProp.floatValue));
        y += line + gap;

        // Row 6: Fire Speed (anim shoot)
        Rect fireSpeedLabelRect = new Rect(rect.x, y, labelW, line);
        Rect fireSpeedFieldRect = new Rect(fireSpeedLabelRect.xMax + gap, y, rect.width - labelW - gap, line);
        EditorGUI.LabelField(fireSpeedLabelRect, "Fire Speed");
        fireSpeedProp.floatValue = Mathf.Max(0f, EditorGUI.FloatField(fireSpeedFieldRect, fireSpeedProp.floatValue));
        y += line + gap;

        // Row 7: Damage + Add Inventory
        Rect dmgLabelRect = new Rect(rect.x, y, labelW, line);
        Rect dmgFieldRect = new Rect(dmgLabelRect.xMax + gap, y, rect.width - labelW - gap - buttonW - gap, line);
        Rect addRect = new Rect(rect.x + rect.width - buttonW, y, buttonW, line);
        EditorGUI.LabelField(dmgLabelRect, "Damage");
        damageProp.floatValue = EditorGUI.FloatField(dmgFieldRect, damageProp.floatValue);

        EditorGUI.BeginDisabledGroup(gunProp.objectReferenceValue == null);
        if (GUI.Button(addRect, "Add"))
            AddGunToUserInventory(gunProp.objectReferenceValue as GameObject);
        EditorGUI.EndDisabledGroup();
    }

    static void DrawIconRow(
        float x,
        float y,
        float totalWidth,
        float labelW,
        float buttonW,
        float gap,
        float line,
        SerializedProperty iconProp,
        System.Action onDelete)
    {
        Rect previewRect = new Rect(x, y, IconPreviewSize, IconPreviewSize);
        EditorGUI.DrawRect(previewRect, new Color(0.18f, 0.18f, 0.18f, 1f));

        Sprite icon = iconProp.objectReferenceValue as Sprite;
        if (icon != null && icon.texture != null)
        {
            // Draw the sprite's UV region from the atlas / texture
            Rect texRect = icon.textureRect;
            Rect uv = new Rect(
                texRect.x / icon.texture.width,
                texRect.y / icon.texture.height,
                texRect.width / icon.texture.width,
                texRect.height / icon.texture.height);
            GUI.DrawTextureWithTexCoords(previewRect, icon.texture, uv, true);
        }
        else
        {
            GUI.Label(previewRect, "No Icon", EditorStyles.centeredGreyMiniLabel);
        }

        // Preview outline so the icon stands out
        Handles.BeginGUI();
        Handles.color = new Color(0.55f, 0.55f, 0.55f, 1f);
        Handles.DrawSolidRectangleWithOutline(previewRect, Color.clear, Handles.color);
        Handles.EndGUI();

        float fieldX = previewRect.xMax + gap;
        float fieldW = totalWidth - IconPreviewSize - gap - buttonW - gap;
        Rect labelRect = new Rect(fieldX, y, labelW, line);
        Rect fieldRect = new Rect(labelRect.xMax + gap, y, fieldW - labelW - gap, line);
        EditorGUI.LabelField(labelRect, "Icon");
        iconProp.objectReferenceValue = EditorGUI.ObjectField(
            fieldRect,
            iconProp.objectReferenceValue,
            typeof(Sprite),
            false);

        Rect deleteRect = new Rect(x + totalWidth - buttonW, y, buttonW, line);
        if (GUI.Button(deleteRect, "Delete"))
            onDelete?.Invoke();
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

    void DrawSfxDropdown(Rect position, SerializedProperty soundProp)
    {
        if (_soundConfig == null || _soundConfig.SfxList == null || _soundConfig.SfxList.Count == 0)
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUI.Popup(position, 0, new[] { "(No SFX)" });
            EditorGUI.EndDisabledGroup();
            return;
        }

        var sfxList = _soundConfig.SfxList;
        string[] options = new string[sfxList.Count + 1];
        options[0] = "(None)";
        int selected = 0;

        for (int i = 0; i < sfxList.Count; i++)
        {
            SoundEntry entry = sfxList[i];
            string name = entry != null && !string.IsNullOrEmpty(entry.Name)
                ? entry.Name
                : $"(SFX {i})";
            options[i + 1] = name;

            if (entry != null && !string.IsNullOrEmpty(entry.Name) && soundProp.stringValue == entry.Name)
                selected = i + 1;
        }

        EditorGUI.BeginChangeCheck();
        int next = EditorGUI.Popup(position, selected, options);
        if (EditorGUI.EndChangeCheck())
        {
            if (next <= 0)
                soundProp.stringValue = string.Empty;
            else
            {
                SoundEntry entry = sfxList[next - 1];
                soundProp.stringValue = entry != null ? entry.Name : string.Empty;
            }
        }
    }

    void AddGunToUserInventory(GameObject gunPrefab)
    {
        if (gunPrefab == null)
            return;

        if (_userConfig == null)
        {
            _userConfig = AssetDatabase.LoadAssetAtPath<UserConfig>(UserConfigAssetPath);
            if (_userConfig == null)
            {
                EditorUtility.DisplayDialog(
                    "UserConfig",
                    "UserConfig.asset was not found.",
                    "OK");
                return;
            }
        }

        if (_userConfig.Inventory == null)
            _userConfig.Inventory = new System.Collections.Generic.List<GameObject>();

        Undo.RecordObject(_userConfig, "Add Weapon To Inventory");
        _userConfig.Inventory.Add(gunPrefab);
        EditorUtility.SetDirty(_userConfig);
        _userConfig.Save();
        AssetDatabase.SaveAssets();

        Debug.Log($"Added \"{gunPrefab.name}\" to UserConfig.Inventory.");
    }

    void TryDeleteWeapon(SerializedProperty listProp, int index)
    {
        SerializedProperty element = listProp.GetArrayElementAtIndex(index);
        SerializedProperty gunProp = element.FindPropertyRelative("_gunPrefab");
        string displayName = gunProp.objectReferenceValue != null
            ? gunProp.objectReferenceValue.name
            : $"(Weapon {index})";

        bool confirmed = EditorUtility.DisplayDialog(
            "Delete Weapon",
            $"Are you sure you want to remove \"{displayName}\" from WeaponConfig?",
            "Delete",
            "Cancel");

        if (!confirmed)
            return;

        listProp.DeleteArrayElementAtIndex(index);
        _serializedConfig.ApplyModifiedProperties();
        EditorUtility.SetDirty(_weaponConfig);
        RebuildReorderableList();
        Repaint();
    }
}
