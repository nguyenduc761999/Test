using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// One level: zombie spawn interval + play time + zombie prefab list (random spawn).
/// </summary>
[Serializable]
[Preserve]
public class LevelEntry
{
    [SerializeField] float _timeSpawnZombie = 5f;
    [SerializeField] float _playTime = 180f;
    [SerializeField] List<GameObject> _zombiePrefabs = new List<GameObject>();

    public float TimeSpawnZombie
    {
        get => _timeSpawnZombie;
        set => _timeSpawnZombie = Mathf.Max(0f, value);
    }

    /// <summary>Level countdown duration in seconds.</summary>
    public float PlayTime
    {
        get => _playTime;
        set => _playTime = Mathf.Max(0f, value);
    }

    public List<GameObject> ZombiePrefabs => _zombiePrefabs;

    /// <summary>
    /// Picks a random zombie prefab from the list (skips null).
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
/// Level config list — add/remove entries in the Inspector or via LevelManager.
/// </summary>
[CreateAssetMenu(fileName = "LevelConfig", menuName = "Test/Level Config", order = 4)]
[Preserve]
public class LevelConfig : ScriptableObject
{
    [SerializeField] List<LevelEntry> _levels = new List<LevelEntry>();

    public List<LevelEntry> Levels => _levels;

    /// <summary>
    /// Gets a level by index (matches UserConfig.currentLevel).
    /// </summary>
    public LevelEntry GetLevel(int index)
    {
        if (_levels == null || index < 0 || index >= _levels.Count)
            return null;

        return _levels[index];
    }

    /// <summary>
    /// Adds a level to the list.
    /// </summary>
    public LevelEntry Add(float timeSpawnZombie, float playTime = 180f, List<GameObject> zombiePrefabs = null)
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
    /// Removes an entry by index.
    /// </summary>
    public bool RemoveAt(int index)
    {
        if (_levels == null || index < 0 || index >= _levels.Count)
            return false;

        _levels.RemoveAt(index);
        return true;
    }
}
