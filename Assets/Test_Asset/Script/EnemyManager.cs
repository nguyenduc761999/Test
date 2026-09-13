using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Runtime EnemyConfig manager: add/remove entries and look up damage/health by prefab.
/// </summary>
public class EnemyManager : MonoBehaviour
{
    [SerializeField] EnemyConfig _enemyConfig;

#if UNITY_EDITOR
    const string EnemyConfigAssetPath = "Assets/Test_Asset/Config/EnemyConfig.asset";
#endif

    public EnemyConfig EnemyConfig => _enemyConfig;

    public List<EnemyEntry> Enemies => _enemyConfig != null ? _enemyConfig.Enemies : null;

    /// <summary>
    /// Adds an entry (prefab + damage + health) to EnemyConfig.
    /// </summary>
    public EnemyEntry AddEnemy(GameObject prefab, float damage, float health = 100f)
    {
        if (_enemyConfig == null)
            return null;

        return _enemyConfig.Add(prefab, damage, health);
    }

    /// <summary>
    /// Removes an entry from EnemyConfig by index.
    /// </summary>
    public bool RemoveEnemy(int index)
    {
        if (_enemyConfig == null)
            return false;

        return _enemyConfig.RemoveAt(index);
    }

    /// <summary>
    /// Gets the entry for the original prefab.
    /// </summary>
    public EnemyEntry FindByPrefab(GameObject prefab)
    {
        return _enemyConfig != null ? _enemyConfig.FindByPrefab(prefab) : null;
    }

    /// <summary>
    /// Gets the entry for a spawned instance.
    /// </summary>
    public EnemyEntry FindByInstance(GameObject instance)
    {
        return _enemyConfig != null ? _enemyConfig.FindByInstance(instance) : null;
    }

#if UNITY_EDITOR
    void Reset()
    {
        if (_enemyConfig == null)
            _enemyConfig = AssetDatabase.LoadAssetAtPath<EnemyConfig>(EnemyConfigAssetPath);
    }

    void OnValidate()
    {
        if (_enemyConfig == null)
            _enemyConfig = AssetDatabase.LoadAssetAtPath<EnemyConfig>(EnemyConfigAssetPath);
    }
#endif
}
