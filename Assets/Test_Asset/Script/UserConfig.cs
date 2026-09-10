using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// Dữ liệu save/load của UserConfig (JSON tại persistentDataPath).
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
/// Cấu hình tiến trình người chơi: điểm, level, inventory súng.
/// </summary>
[CreateAssetMenu(fileName = "UserConfig", menuName = "Test/User Config", order = 1)]
[Preserve]
public class UserConfig : ScriptableObject
{
    public int currentScore;
    public int highScore;
    public int currentLevel;
    /// <summary>Level user đang ở (1-based, tăng liên tục khi Next Level kể cả sau level config cuối).</summary>
    public int level = 1;
    public int health;
    /// <summary>Chỉ số phần tử Inventory đang dùng làm vũ khí.</summary>
    public int WeaponUse;
    public List<GameObject> Inventory = new List<GameObject>();
    /// <summary>Prefab lựu đạn trong inventory.</summary>
    public GameObject grenade;

    /// <summary>Prefab grenade đã gán trên asset — giữ khi Load để match lại theo tên.</summary>
    GameObject _grenadeAssetRef;

    const string SaveFileName = "userconfig.json";

    static string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

    void OnEnable()
    {
        if (grenade != null)
            _grenadeAssetRef = grenade;
    }

    /// <summary>
    /// Lưu toàn bộ field UserConfig ra persistentDataPath.
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
            Debug.LogWarning($"UserConfig.Save thất bại: {e.Message}");
        }
    }

    /// <summary>
    /// Khôi phục toàn bộ field UserConfig từ persistentDataPath.
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
            // Save cũ chưa có level → suy từ currentLevel (0-based)
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
            Debug.LogWarning($"UserConfig.Load thất bại: {e.Message}");
        }
    }

    /// <summary>
    /// Khôi phục grenade theo tên đã lưu, giữ reference prefab trên asset.
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
    /// Sắp xếp lại Inventory theo tên đã lưu, giữ reference prefab trên asset.
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

        // Giữ phần tử còn lại trên asset (chưa có trong file save)
        for (int i = 0; i < source.Count; i++)
            Inventory.Add(source[i]);
    }

    /// <summary>
    /// Đồng bộ level (1-based) với currentLevel (0-based progress).
    /// </summary>
    public void SyncLevelFields()
    {
        if (level < 1)
            level = Mathf.Max(1, currentLevel + 1);

        currentLevel = level - 1;
    }

    /// <summary>
    /// Giữ WeaponUse trong phạm vi Inventory.
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
    /// Menu ba chấm trên Inspector → Save thay đổi ra persistentDataPath.
    /// </summary>
    [ContextMenu("Save")]
    void SaveFromInspector()
    {
        SyncLevelFields();
        ClampWeaponUse();
        Save();
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();
        Debug.Log($"UserConfig đã Save: {SavePath}");
    }
#endif
}
