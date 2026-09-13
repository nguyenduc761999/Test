using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Runtime LevelConfig manager: add/remove entries and look up a level by index.
/// </summary>
public class LevelManager : MonoBehaviour
{
    [SerializeField] LevelConfig _levelConfig;

#if UNITY_EDITOR
    const string LevelConfigAssetPath = "Assets/Test_Asset/Config/LevelConfig.asset";
#endif

    public LevelConfig LevelConfig => _levelConfig;

    public List<LevelEntry> Levels => _levelConfig != null ? _levelConfig.Levels : null;

    /// <summary>
    /// Gets a level by index (mapped from UserConfig.level via modulo).
    /// </summary>
    public LevelEntry GetLevel(int index)
    {
        return _levelConfig != null ? _levelConfig.GetLevel(index) : null;
    }

    /// <summary>
    /// Adds an entry (timeSpawnZombie + playTime + prefab list) to LevelConfig.
    /// </summary>
    public LevelEntry AddLevel(float timeSpawnZombie, float playTime = 180f, List<GameObject> zombiePrefabs = null)
    {
        if (_levelConfig == null)
            return null;

        return _levelConfig.Add(timeSpawnZombie, playTime, zombiePrefabs);
    }

    /// <summary>
    /// Removes an entry from LevelConfig by index.
    /// </summary>
    public bool RemoveLevel(int index)
    {
        if (_levelConfig == null)
            return false;

        return _levelConfig.RemoveAt(index);
    }

#if UNITY_EDITOR
    void Reset()
    {
        if (_levelConfig == null)
            _levelConfig = AssetDatabase.LoadAssetAtPath<LevelConfig>(LevelConfigAssetPath);
    }

    void OnValidate()
    {
        if (_levelConfig == null)
            _levelConfig = AssetDatabase.LoadAssetAtPath<LevelConfig>(LevelConfigAssetPath);
    }
#endif
}
