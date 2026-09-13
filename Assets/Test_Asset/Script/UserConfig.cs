using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// UserConfig save/load payload (JSON at persistentDataPath).
/// </summary>
[Serializable]
[Preserve]
public class UserConfigSaveData
{
    public int currentScore;
    public int highScore;
    public int currentLevel;
    public int level;
    public int health;
    public int WeaponUse;
    public List<string> inventoryNames = new List<string>();
    public string grenadeName;
}

/// <summary>
/// Player progress config: score, level, gun inventory.
/// </summary>
[CreateAssetMenu(fileName = "UserConfig", menuName = "Test/User Config", order = 1)]
[Preserve]
public class UserConfig : ScriptableObject
{
    public int currentScore = 0;
    public int highScore = 0;
    public int currentLevel = 0;
    /// <summary>Current user level (1-based, keeps increasing on Next Level even past the last config entry).</summary>
    public int level = 1;
    public int health = 100;
    /// <summary>Inventory index currently equipped as the weapon.</summary>
    public int WeaponUse = 0;
    public List<GameObject> Inventory = new List<GameObject>();
    /// <summary>Grenade prefab in inventory.</summary>
    public GameObject grenade;

    /// <summary>Grenade prefab assigned on the asset — kept on Load so it can be rematched by name.</summary>
    GameObject _grenadeAssetRef;

    const string SaveFileName = "userconfig.json";

    static string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

    void OnEnable()
    {
        if (grenade != null)
            _grenadeAssetRef = grenade;
    }

    /// <summary>
    /// Resets score/level/health to initial values. Inventory (3 guns) and grenade stay unchanged.
    /// </summary>
    public void ResetToInitial()
    {
        currentScore = 0;
        highScore = 0;
        currentLevel = 0;
        level = 1;
        health = 100;
        WeaponUse = 0;
        ClampWeaponUse();
        SyncLevelFields();
        Save();
    }

    /// <summary>
    /// Saves every UserConfig field to persistentDataPath.
    /// </summary>
    public void Save()
    {
        SyncLevelFields();

        UserConfigSaveData data = new UserConfigSaveData
        {
            currentScore = currentScore,
            highScore = highScore,
            currentLevel = currentLevel,
            level = level,
            health = health,
            WeaponUse = WeaponUse,
            inventoryNames = new List<string>(),
            grenadeName = grenade != null ? grenade.name : string.Empty
        };

        if (Inventory != null)
        {
            for (int i = 0; i < Inventory.Count; i++)
            {
                GameObject item = Inventory[i];
                data.inventoryNames.Add(item != null ? item.name : string.Empty);
            }
        }

        try
        {
            File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"UserConfig.Save failed: {e.Message}");
        }
    }

    /// <summary>
    /// Restores every UserConfig field from persistentDataPath.
    /// </summary>
    public void Load()
    {
        if (!File.Exists(SavePath))
            return;

        try
        {
            string json = File.ReadAllText(SavePath);
            UserConfigSaveData data = JsonUtility.FromJson<UserConfigSaveData>(json);
            if (data == null)
                return;

            currentScore = data.currentScore;
            highScore = data.highScore;
            currentLevel = data.currentLevel;
            // Older saves have no level field — derive it from currentLevel (0-based)
            level = data.level > 0 ? data.level : Mathf.Max(1, data.currentLevel + 1);
            health = data.health;
            WeaponUse = data.WeaponUse;
            RestoreInventory(data.inventoryNames);
            RestoreGrenade(data.grenadeName);
            ClampWeaponUse();
            SyncLevelFields();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"UserConfig.Load failed: {e.Message}");
        }
    }

    /// <summary>
    /// Restores grenade by saved name, keeping the prefab reference on the asset.
    /// </summary>
    void RestoreGrenade(string savedName)
    {
        if (_grenadeAssetRef == null && grenade != null)
            _grenadeAssetRef = grenade;

        if (string.IsNullOrEmpty(savedName))
        {
            if (_grenadeAssetRef != null)
                grenade = _grenadeAssetRef;
            return;
        }

        if (_grenadeAssetRef != null && _grenadeAssetRef.name == savedName)
        {
            grenade = _grenadeAssetRef;
            return;
        }

        if (grenade != null && grenade.name == savedName)
            return;

        if (_grenadeAssetRef != null)
            grenade = _grenadeAssetRef;
    }

    /// <summary>
    /// Reorders Inventory by saved names, keeping prefab references on the asset.
    /// </summary>
    void RestoreInventory(List<string> savedNames)
    {
        if (savedNames == null || Inventory == null || Inventory.Count == 0)
            return;

        List<GameObject> source = new List<GameObject>(Inventory);
        Inventory.Clear();

        for (int i = 0; i < savedNames.Count; i++)
        {
            string name = savedNames[i];
            if (string.IsNullOrEmpty(name))
            {
                Inventory.Add(null);
                continue;
            }

            GameObject match = null;
            for (int j = 0; j < source.Count; j++)
            {
                GameObject candidate = source[j];
                if (candidate != null && candidate.name == name)
                {
                    match = candidate;
                    source.RemoveAt(j);
                    break;
                }
            }

            Inventory.Add(match);
        }

        // Keep remaining asset entries that were not in the save file
        for (int i = 0; i < source.Count; i++)
            Inventory.Add(source[i]);
    }

    /// <summary>
    /// Syncs level (1-based) with currentLevel (0-based progress).
    /// </summary>
    public void SyncLevelFields()
    {
        if (level < 1)
            level = Mathf.Max(1, currentLevel + 1);

        currentLevel = level - 1;
    }

    /// <summary>
    /// Clamps WeaponUse to the Inventory range.
    /// </summary>
    public void ClampWeaponUse()
    {
        if (Inventory == null || Inventory.Count == 0)
        {
            WeaponUse = 0;
            return;
        }

        if (WeaponUse < 0)
            WeaponUse = 0;
        else if (WeaponUse >= Inventory.Count)
            WeaponUse = Inventory.Count - 1;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Inspector kebab menu → Save changes to persistentDataPath.
    /// </summary>
    [ContextMenu("Save")]
    void SaveFromInspector()
    {
        SyncLevelFields();
        ClampWeaponUse();
        Save();
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();
        Debug.Log($"UserConfig saved: {SavePath}");
    }

    /// <summary>
    /// Inspector kebab menu → reset to initial values, keep 3 guns.
    /// </summary>
    [ContextMenu("Reset To Initial")]
    void ResetToInitialFromInspector()
    {
        ResetToInitial();
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();
        Debug.Log($"UserConfig ResetToInitial, kept {Inventory?.Count ?? 0} guns: {SavePath}");
    }
#endif
}
