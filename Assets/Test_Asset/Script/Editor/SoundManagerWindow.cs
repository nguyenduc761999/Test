using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Test Manager → Sound Manager window: manages SoundConfig (BGM / SFX / Button).
/// </summary>
public class SoundManagerWindow : EditorWindow
{
    const string SoundConfigAssetPath = "Assets/Test_Asset/Config/SoundConfig.asset";
    const float ElementHeight = 52f;

    SoundConfig _soundConfig;
    SoundCategory _currentTab = SoundCategory.BGM;
    ReorderableList _reorderableList;
    Vector2 _scroll;
    SerializedObject _serializedConfig;
    string _listPropertyName;

    [MenuItem("Test Manager/Sound Manager")]
    static void Open()
    {
        var window = GetWindow<SoundManagerWindow>("Sound Manager");
        window.minSize = new Vector2(520f, 400f);
        window.Show();
    }

    void OnEnable()
    {
        LoadOrCreateSoundConfig();
        RebuildReorderableList();
    }

    void OnGUI()
    {
        EditorGUILayout.Space(8f);
        DrawConfigField();
        EditorGUILayout.Space(6f);

        if (_soundConfig == null)
        {
            EditorGUILayout.HelpBox("No SoundConfig assigned. Assign or create one above.", MessageType.Warning);
            return;
        }

        DrawTabs();
        EditorGUILayout.Space(8f);
        DrawDropArea();
        EditorGUILayout.Space(8f);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        _reorderableList?.DoLayoutList();
        EditorGUILayout.EndScrollView();

        if (_serializedConfig != null && _serializedConfig.ApplyModifiedProperties())
            EditorUtility.SetDirty(_soundConfig);
    }

    /// <summary>
    /// SoundConfig object field / create button.
    /// </summary>
    void DrawConfigField()
    {
        EditorGUI.BeginChangeCheck();
        _soundConfig = (SoundConfig)EditorGUILayout.ObjectField(
            "Sound Config",
            _soundConfig,
            typeof(SoundConfig),
            false);

        if (EditorGUI.EndChangeCheck())
            RebuildReorderableList();

        if (_soundConfig == null && GUILayout.Button("Create SoundConfig"))
        {
            LoadOrCreateSoundConfig();
            RebuildReorderableList();
        }
    }

    /// <summary>
    /// Three main tabs: BGM, SFX, Button.
    /// </summary>
    void DrawTabs()
    {
        int selected = GUILayout.Toolbar(
            (int)_currentTab,
            new[] { "BGM", "SFX", "Button" },
            GUILayout.Height(28f));

        var nextTab = (SoundCategory)selected;
        if (nextTab != _currentTab)
        {
            _currentTab = nextTab;
            RebuildReorderableList();
        }
    }

    /// <summary>
    /// Drop zone for one or many AudioClips into the current tab list.
    /// </summary>
    void DrawDropArea()
    {
        var dropRect = GUILayoutUtility.GetRect(0f, 56f, GUILayout.ExpandWidth(true));
        GUI.Box(dropRect, "Drag & drop AudioClips here (one or many)", EditorStyles.helpBox);

        var evt = Event.current;
        if (!dropRect.Contains(evt.mousePosition))
            return;

        switch (evt.type)
        {
            case EventType.DragUpdated:
            case EventType.DragPerform:
                if (!HasAudioClipDrag())
                    break;

                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    AddClipsFromDrag();
                }

                evt.Use();
                break;
        }
    }

    bool HasAudioClipDrag()
    {
        foreach (Object obj in DragAndDrop.objectReferences)
        {
            if (obj is AudioClip)
                return true;
        }

        return false;
    }

    void AddClipsFromDrag()
    {
        if (_soundConfig == null)
            return;

        var list = _soundConfig.GetList(_currentTab);
        if (list == null)
            return;

        Undo.RecordObject(_soundConfig, "Add Sound");
        foreach (Object obj in DragAndDrop.objectReferences)
        {
            if (obj is not AudioClip clip)
                continue;

            if (ContainsClip(list, clip))
                continue;

            list.Add(new SoundEntry(clip));
        }

        EditorUtility.SetDirty(_soundConfig);
        RebuildReorderableList();
        Repaint();
    }

    static bool ContainsClip(System.Collections.Generic.List<SoundEntry> list, AudioClip clip)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null && list[i].Clip == clip)
                return true;
        }

        return false;
    }

    void LoadOrCreateSoundConfig()
    {
        _soundConfig = AssetDatabase.LoadAssetAtPath<SoundConfig>(SoundConfigAssetPath);
        if (_soundConfig != null)
            return;

        string folder = "Assets/Test_Asset/Config";
        if (!AssetDatabase.IsValidFolder(folder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Test_Asset"))
                AssetDatabase.CreateFolder("Assets", "Test_Asset");
            AssetDatabase.CreateFolder("Assets/Test_Asset", "Config");
        }

        _soundConfig = CreateInstance<SoundConfig>();
        AssetDatabase.CreateAsset(_soundConfig, SoundConfigAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    void RebuildReorderableList()
    {
        _reorderableList = null;
        _serializedConfig = null;
        _listPropertyName = null;

        if (_soundConfig == null)
            return;

        _listPropertyName = GetPropertyName(_currentTab);
        _serializedConfig = new SerializedObject(_soundConfig);
        SerializedProperty listProp = _serializedConfig.FindProperty(_listPropertyName);
        if (listProp == null)
            return;

        _reorderableList = new ReorderableList(_serializedConfig, listProp, true, true, false, false);
        _reorderableList.drawHeaderCallback = rect =>
        {
            EditorGUI.LabelField(rect, $"{_currentTab}  ({listProp.arraySize})");
        };

        _reorderableList.elementHeight = ElementHeight;
        _reorderableList.drawElementCallback = (rect, index, active, focused) =>
        {
            DrawSoundElement(rect, listProp, index);
        };
    }

    string GetPropertyName(SoundCategory category)
    {
        switch (category)
        {
            case SoundCategory.BGM:
                return "_bgmList";
            case SoundCategory.SFX:
                return "_sfxList";
            case SoundCategory.Button:
                return "_buttonList";
            default:
                return "_bgmList";
        }
    }

    /// <summary>
    /// Draws each entry: editable name, volume, view, delete; drag handle to reorder.
    /// </summary>
    void DrawSoundElement(Rect rect, SerializedProperty listProp, int index)
    {
        if (index < 0 || index >= listProp.arraySize)
            return;

        rect.y += 2f;
        float line = EditorGUIUtility.singleLineHeight;
        float gap = 4f;
        float buttonWidth = 52f;

        SerializedProperty element = listProp.GetArrayElementAtIndex(index);
        SerializedProperty nameProp = element.FindPropertyRelative("_name");
        SerializedProperty clipProp = element.FindPropertyRelative("_clip");
        SerializedProperty volumeProp = element.FindPropertyRelative("_volume");
        var clip = clipProp.objectReferenceValue as AudioClip;

        // Row 1: Name + View + Delete
        Rect nameLabelRect = new Rect(rect.x, rect.y, 40f, line);
        Rect nameFieldRect = new Rect(
            nameLabelRect.xMax + gap,
            rect.y,
            rect.width - 40f - gap - (buttonWidth * 2f + gap * 2f),
            line);
        Rect viewRect = new Rect(nameFieldRect.xMax + gap, rect.y, buttonWidth, line);
        Rect deleteRect = new Rect(viewRect.xMax + gap, rect.y, buttonWidth, line);

        EditorGUI.LabelField(nameLabelRect, "Name");
        nameProp.stringValue = EditorGUI.TextField(nameFieldRect, nameProp.stringValue);

        EditorGUI.BeginDisabledGroup(clip == null);
        if (GUI.Button(viewRect, "View"))
            ViewSound(clip);
        EditorGUI.EndDisabledGroup();

        string displayName = !string.IsNullOrEmpty(nameProp.stringValue)
            ? nameProp.stringValue
            : (clip != null ? clip.name : "(empty)");

        if (GUI.Button(deleteRect, "Delete"))
            TryDeleteSound(listProp, index, displayName);

        // Row 2: Clip + Volume
        float row2Y = rect.y + line + 4f;
        Rect clipLabelRect = new Rect(rect.x, row2Y, 40f, line);
        float volumeBlockWidth = 160f;
        Rect clipFieldRect = new Rect(
            clipLabelRect.xMax + gap,
            row2Y,
            rect.width - 40f - gap - volumeBlockWidth - gap,
            line);
        Rect volumeRect = new Rect(clipFieldRect.xMax + gap, row2Y, volumeBlockWidth, line);

        EditorGUI.LabelField(clipLabelRect, "Clip");
        EditorGUI.BeginDisabledGroup(true);
        EditorGUI.ObjectField(clipFieldRect, clip, typeof(AudioClip), false);
        EditorGUI.EndDisabledGroup();

        volumeProp.floatValue = EditorGUI.Slider(volumeRect, volumeProp.floatValue, 0f, 1f);
    }

    void ViewSound(AudioClip clip)
    {
        if (clip == null)
            return;

        Selection.activeObject = clip;
        EditorGUIUtility.PingObject(clip);
        PlayClipPreview(clip);
    }

    /// <summary>
    /// Plays a clip preview in the Editor (Unity internal AudioUtil).
    /// </summary>
    static void PlayClipPreview(AudioClip clip)
    {
        if (clip == null)
            return;

        System.Type audioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        if (audioUtil == null)
            return;

        MethodInfo playMethod = audioUtil.GetMethod(
            "PlayPreviewClip",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new[] { typeof(AudioClip), typeof(int), typeof(bool) },
            null);

        if (playMethod == null)
        {
            playMethod = audioUtil.GetMethod(
                "PlayClip",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { typeof(AudioClip) },
                null);
            playMethod?.Invoke(null, new object[] { clip });
            return;
        }

        playMethod.Invoke(null, new object[] { clip, 0, false });
    }

    void TryDeleteSound(SerializedProperty listProp, int index, string displayName)
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "Delete Sound",
            $"Are you sure you want to remove \"{displayName}\" from the {_currentTab} list?",
            "Delete",
            "Cancel");

        if (!confirmed)
            return;

        listProp.DeleteArrayElementAtIndex(index);
        _serializedConfig.ApplyModifiedProperties();
        EditorUtility.SetDirty(_soundConfig);
        RebuildReorderableList();
        Repaint();
    }
}

/// <summary>
/// Dropdown that picks an SFX name from SoundConfig (_soundConfig) on the same object.
/// </summary>
[CustomPropertyDrawer(typeof(SfxSoundAttribute))]
public class SfxSoundDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        SoundConfig soundConfig = null;
        SerializedProperty soundConfigProp = property.serializedObject.FindProperty("_soundConfig");
        if (soundConfigProp != null)
            soundConfig = soundConfigProp.objectReferenceValue as SoundConfig;

        if (soundConfig == null || soundConfig.SfxList == null || soundConfig.SfxList.Count == 0)
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUI.Popup(position, label.text, 0, new[] { "(No SFX)" });
            EditorGUI.EndDisabledGroup();
            return;
        }

        var sfxList = soundConfig.SfxList;
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

            if (entry != null && !string.IsNullOrEmpty(entry.Name) && property.stringValue == entry.Name)
                selected = i + 1;
        }

        EditorGUI.BeginChangeCheck();
        int next = EditorGUI.Popup(position, label.text, selected, options);
        if (EditorGUI.EndChangeCheck())
        {
            if (next <= 0)
                property.stringValue = string.Empty;
            else
            {
                SoundEntry entry = sfxList[next - 1];
                property.stringValue = entry != null ? entry.Name : string.Empty;
            }
        }
    }
}
