using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Quản lý LevelConfig runtime: thêm/xóa phần tử, tra cứu level theo chỉ số.
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
    /// Lấy level theo chỉ số (map từ UserConfig.level qua modulo).
    /// </summary>
    public LevelEntry GetLevel(int index)
    {
        return _levelConfig != null ? _levelConfig.GetLevel(index) : null;
    }

    /// <summary>
    /// Thêm phần tử (timeSpawnZombie + playTime + list prefab) vào LevelConfig.
    /// </summary>
    public LevelEntry AddLevel(float timeSpawnZombie, float playTime = 60f, List<GameObject> zombiePrefabs = null)
    {
        if (_levelConfig == null)
            return null;

        return _levelConfig.Add(timeSpawnZombie, playTime, zombiePrefabs);
    }

    /// <summary>
    /// Xóa phần tử theo chỉ số trong LevelConfig.
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
