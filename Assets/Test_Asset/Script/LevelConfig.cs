using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// Một level: thời gian spawn zombie + thời gian chơi + danh sách prefab zombie (spawn random).
/// </summary>
[Serializable]
[Preserve]
public class LevelEntry
{
    [SerializeField] float _timeSpawnZombie = 5f;
    [SerializeField] float _playTime = 60f;
    [SerializeField] List<GameObject> _zombiePrefabs = new List<GameObject>();

    public float TimeSpawnZombie
    {
        get => _timeSpawnZombie;
        set => _timeSpawnZombie = Mathf.Max(0f, value);
    }

    /// <summary>Thời gian chơi đếm ngược (giây) của level.</summary>
    public float PlayTime
    {
        get => _playTime;
        set => _playTime = Mathf.Max(0f, value);
    }

    public List<GameObject> ZombiePrefabs => _zombiePrefabs;

    /// <summary>
    /// Lấy ngẫu nhiên 1 prefab zombie trong list (bỏ qua null).
    /// </summary>
    public GameObject GetRandomZombiePrefab()
    {
        if (_zombiePrefabs == null || _zombiePrefabs.Count == 0)
            return null;

        int validCount = 0;
        for (int i = 0; i < _zombiePrefabs.Count; i++)
        {
            if (_zombiePrefabs[i] != null)
                validCount++;
        }

        if (validCount == 0)
            return null;

        int pick = UnityEngine.Random.Range(0, validCount);
        for (int i = 0; i < _zombiePrefabs.Count; i++)
        {
            GameObject prefab = _zombiePrefabs[i];
            if (prefab == null)
                continue;

            if (pick == 0)
                return prefab;

            pick--;
        }

        return null;
    }
}

/// <summary>
/// Danh sách cấu hình level — thêm/xóa phần tử trên Inspector hoặc qua LevelManager.
/// </summary>
[CreateAssetMenu(fileName = "LevelConfig", menuName = "Test/Level Config", order = 4)]
[Preserve]
public class LevelConfig : ScriptableObject
{
    [SerializeField] List<LevelEntry> _levels = new List<LevelEntry>();

    public List<LevelEntry> Levels => _levels;

    /// <summary>
    /// Lấy level theo chỉ số (khớp UserConfig.currentLevel).
    /// </summary>
    public LevelEntry GetLevel(int index)
    {
        if (_levels == null || index < 0 || index >= _levels.Count)
            return null;

        return _levels[index];
    }

    /// <summary>
    /// Thêm một level vào danh sách.
    /// </summary>
    public LevelEntry Add(float timeSpawnZombie, float playTime = 60f, List<GameObject> zombiePrefabs = null)
    {
        if (_levels == null)
            _levels = new List<LevelEntry>();

        LevelEntry entry = new LevelEntry
        {
            TimeSpawnZombie = timeSpawnZombie,
            PlayTime = playTime
        };

        if (zombiePrefabs != null)
        {
            for (int i = 0; i < zombiePrefabs.Count; i++)
            {
                if (zombiePrefabs[i] != null)
                    entry.ZombiePrefabs.Add(zombiePrefabs[i]);
            }
        }

        _levels.Add(entry);
        return entry;
    }

    /// <summary>
    /// Xóa phần tử theo chỉ số.
    /// </summary>
    public bool RemoveAt(int index)
    {
        if (_levels == null || index < 0 || index >= _levels.Count)
            return false;

        _levels.RemoveAt(index);
        return true;
    }
}
