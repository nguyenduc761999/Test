using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// Một vũ khí: icon, gun, bullet, VFX, sound SFX, damage, bullet speed, fire speed anim.
/// </summary>
[Serializable]
[Preserve]
public class WeaponEntry
{
    [SerializeField] Sprite _icon;
    [SerializeField] GameObject _gunPrefab;
    [SerializeField] GameObject _bulletPrefab;
    [SerializeField] GameObject _fireVfx;
    [SerializeField] string _sound = string.Empty;
    [SerializeField] float _damage = 10f;
    [SerializeField] float _bulletSpeed = 20f;
    [SerializeField] float _fireSpeed = 1f;

    public Sprite Icon
    {
        get => _icon;
        set => _icon = value;
    }

    public GameObject GunPrefab
    {
        get => _gunPrefab;
        set => _gunPrefab = value;
    }

    public GameObject BulletPrefab
    {
        get => _bulletPrefab;
        set => _bulletPrefab = value;
    }

    public GameObject FireVfx
    {
        get => _fireVfx;
        set => _fireVfx = value;
    }

    public string Sound
    {
        get => _sound;
        set => _sound = value ?? string.Empty;
    }

    public float Damage
    {
        get => _damage;
        set => _damage = Mathf.Max(0f, value);
    }

    public float BulletSpeed
    {
        get => _bulletSpeed;
        set => _bulletSpeed = Mathf.Max(0f, value);
    }

    /// <summary>Tốc độ phát anim infantry_combat_shoot.</summary>
    public float FireSpeed
    {
        get => _fireSpeed;
        set => _fireSpeed = Mathf.Max(0f, value);
    }
}

/// <summary>
/// Danh sách cấu hình vũ khí dùng bởi Weapon Manager / Player.
/// </summary>
[CreateAssetMenu(fileName = "WeaponConfig", menuName = "Test/Weapon Config", order = 2)]
[Preserve]
public class WeaponConfig : ScriptableObject
{
    [SerializeField] List<WeaponEntry> _weapons = new List<WeaponEntry>();

    public List<WeaponEntry> Weapons => _weapons;

    /// <summary>
    /// Tìm weapon theo prefab súng (so khớp reference).
    /// </summary>
    public WeaponEntry FindByGunPrefab(GameObject gunPrefab)
    {
        if (gunPrefab == null || _weapons == null)
            return null;

        for (int i = 0; i < _weapons.Count; i++)
        {
            WeaponEntry entry = _weapons[i];
            if (entry != null && entry.GunPrefab == gunPrefab)
                return entry;
        }

        return null;
    }
}
