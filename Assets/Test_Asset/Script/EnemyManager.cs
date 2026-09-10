using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Quản lý EnemyConfig runtime: thêm/xóa phần tử, tra cứu damage/health theo prefab.
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
    /// Thêm phần tử (prefab + damage + health) vào EnemyConfig.
    /// </summary>
    public EnemyEntry AddEnemy(GameObject prefab, float damage, float health = 100f)
    {
        if (_enemyConfig == null)
            return null;

        return _enemyConfig.Add(prefab, damage, health);
    }

    /// <summary>
    /// Xóa phần tử theo chỉ số trong EnemyConfig.
    /// </summary>
    public bool RemoveEnemy(int index)
    {
        if (_enemyConfig == null)
            return false;

        return _enemyConfig.RemoveAt(index);
    }

    /// <summary>
    /// Lấy entry theo prefab gốc.
    /// </summary>
    public EnemyEntry FindByPrefab(GameObject prefab)
    {
        return _enemyConfig != null ? _enemyConfig.FindByPrefab(prefab) : null;
    }

    /// <summary>
    /// Lấy entry theo instance đang spawn.
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
