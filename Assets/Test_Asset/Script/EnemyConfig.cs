using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// Một loại enemy: prefab + damage + health.
/// </summary>
[Serializable]
[Preserve]
public class EnemyEntry
{
    [SerializeField] GameObject _prefab;
    [SerializeField] float _damage = 10f;
    [SerializeField] float _health = 100f;

    public GameObject Prefab
    {
        get => _prefab;
        set => _prefab = value;
    }

    public float Damage
    {
        get => _damage;
        set => _damage = Mathf.Max(0f, value);
    }

    public float Health
    {
        get => _health;
        set => _health = Mathf.Max(0f, value);
    }
}

/// <summary>
/// Danh sách cấu hình enemy — thêm/xóa phần tử trên Inspector hoặc qua EnemyManager.
/// </summary>
[CreateAssetMenu(fileName = "EnemyConfig", menuName = "Test/Enemy Config", order = 3)]
[Preserve]
public class EnemyConfig : ScriptableObject
{
    [SerializeField] List<EnemyEntry> _enemies = new List<EnemyEntry>();

    public List<EnemyEntry> Enemies => _enemies;

    /// <summary>
    /// Thêm một enemy vào danh sách.
    /// </summary>
    public EnemyEntry Add(GameObject prefab, float damage, float health = 100f)
    {
        if (_enemies == null)
            _enemies = new List<EnemyEntry>();

        EnemyEntry entry = new EnemyEntry
        {
            Prefab = prefab,
            Damage = damage,
            Health = health
        };
        _enemies.Add(entry);
        return entry;
    }

    /// <summary>
    /// Xóa phần tử theo chỉ số.
    /// </summary>
    public bool RemoveAt(int index)
    {
        if (_enemies == null || index < 0 || index >= _enemies.Count)
            return false;

        _enemies.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Tìm theo prefab (so khớp reference).
    /// </summary>
    public EnemyEntry FindByPrefab(GameObject prefab)
    {
        if (prefab == null || _enemies == null)
            return null;

        for (int i = 0; i < _enemies.Count; i++)
        {
            EnemyEntry entry = _enemies[i];
            if (entry != null && entry.Prefab == prefab)
                return entry;
        }

        return null;
    }

    /// <summary>
    /// Tìm theo instance spawn (khớp tên prefab, bỏ hậu tố Clone).
    /// </summary>
    public EnemyEntry FindByInstance(GameObject instance)
    {
        if (instance == null || _enemies == null)
            return null;

        string instanceName = StripCloneSuffix(instance.name);
        for (int i = 0; i < _enemies.Count; i++)
        {
            EnemyEntry entry = _enemies[i];
            if (entry == null || entry.Prefab == null)
                continue;

            if (entry.Prefab.name == instanceName)
                return entry;
        }

        return null;
    }

    static string StripCloneSuffix(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return string.Empty;

        const string cloneSuffix = "(Clone)";
        int index = objectName.IndexOf(cloneSuffix, StringComparison.Ordinal);
        if (index < 0)
            return objectName.Trim();

        return objectName.Substring(0, index).Trim();
    }

    /// <summary>
    /// Gây damage lên player — trừ health của PlayerController theo Damage của enemy instance.
    /// </summary>
    public void AttackPlayer(PlayerController player, GameObject enemyInstance)
    {
        if (player == null || enemyInstance == null)
            return;

        EnemyEntry entry = FindByInstance(enemyInstance);
        if (entry == null)
            return;

        // Truyền transform Zombie để Player spawn hitVFX ngược hướng tấn công
        player.TakeDamage(entry.Damage, enemyInstance.transform);
    }
}
