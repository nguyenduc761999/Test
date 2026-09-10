using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class GameplayManager : MonoBehaviour
{
    public List<Transform> locationSpawnZomebie;

    public TMP_Text playTime;
    public TMP_Text levelTxt;
    public GameObject winUI;
    public GameObject loseUI;

    [SerializeField] LevelManager _levelManager;
    [SerializeField] UserConfig _userConfig;

    float _timeSpawnInterval;
    float _timeSpawnCountdown;
    float _playTimeCountdown;
    bool _gameEnded;

    PlayerController _playerController;
    Transform _uiParent;

#if UNITY_EDITOR
    const string UserConfigAssetPath = "Assets/Test_Asset/Config/UserConfig.asset";
#endif

    void Awake()
    {
        // Cache Player + Canvas (không Find trong Update)
        _playerController = FindFirstObjectByType<PlayerController>();
        Canvas canvas = FindFirstObjectByType<Canvas>();
        _uiParent = canvas != null ? canvas.transform : null;

        if (_userConfig != null)
        {
            _userConfig.Load();
            _userConfig.SyncLevelFields();
        }

        RefreshSpawnIntervalFromLevel();
        _timeSpawnCountdown = _timeSpawnInterval;

        // playTime lấy từ LevelConfig theo level user đang ở
        LevelEntry level = GetCurrentLevel();
        _playTimeCountdown = level != null ? level.PlayTime : 0f;

        RefreshLevelTxt();
        RefreshPlayTimeTxt();
    }

    void Update()
    {
        if (_gameEnded)
            return;

        // Player chết trước khi hết giờ → LoseUI
        if (_playerController != null && _playerController.IsDead)
        {
            ShowLoseUI();
            return;
        }

        TickPlayTime();
        TickZombieSpawn();
    }

    /// <summary>
    /// Đếm ngược playTime; về 0 và Player còn sống → WinUI.
    /// </summary>
    void TickPlayTime()
    {
        if (_playTimeCountdown <= 0f)
            return;

        _playTimeCountdown -= Time.deltaTime;
        if (_playTimeCountdown <= 0f)
        {
            _playTimeCountdown = 0f;
            RefreshPlayTimeTxt();

            if (_playerController == null || !_playerController.IsDead)
                ShowWinUI();
            return;
        }

        RefreshPlayTimeTxt();
    }

    void TickZombieSpawn()
    {
        if (_timeSpawnInterval <= 0f)
            return;

        LevelEntry level = GetCurrentLevel();
        if (level == null)
            return;

        // Đếm ngược rồi spawn tại location random + prefab zombie random trong list level
        _timeSpawnCountdown -= Time.deltaTime;
        if (_timeSpawnCountdown > 0f)
            return;

        SpawnZombies(level);
        _timeSpawnCountdown = _timeSpawnInterval;
    }

    /// <summary>
    /// Đọc timeSpawnZombie từ level hiện tại (map theo UserConfig.level).
    /// </summary>
    void RefreshSpawnIntervalFromLevel()
    {
        LevelEntry level = GetCurrentLevel();
        _timeSpawnInterval = level != null ? level.TimeSpawnZombie : 0f;
    }

    /// <summary>
    /// Level config theo level user (wrap vòng khi vượt quá số phần tử LevelConfig).
    /// </summary>
    LevelEntry GetCurrentLevel()
    {
        if (_levelManager == null || _userConfig == null)
            return null;

        List<LevelEntry> levels = _levelManager.Levels;
        if (levels == null || levels.Count == 0)
            return null;

        int index = (_userConfig.level - 1) % levels.Count;
        if (index < 0)
            index = 0;

        return _levelManager.GetLevel(index);
    }

    void SpawnZombies(LevelEntry level)
    {
        if (level == null)
            return;

        if (locationSpawnZomebie == null || locationSpawnZomebie.Count == 0)
            return;

        GameObject zombiePrefab = level.GetRandomZombiePrefab();
        if (zombiePrefab == null)
            return;

        // Random 1 location trong list để spawn
        Transform location = locationSpawnZomebie[Random.Range(0, locationSpawnZomebie.Count)];
        if (location == null)
            return;

        Instantiate(zombiePrefab, location.position, location.rotation);
    }

    void RefreshLevelTxt()
    {
        if (levelTxt == null || _userConfig == null)
            return;

        levelTxt.text = $"Level {_userConfig.level}";
    }

    void RefreshPlayTimeTxt()
    {
        if (playTime == null)
            return;

        // Hiển thị mm:ss
        int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(_playTimeCountdown));
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        playTime.text = $"{minutes:00}:{seconds:00}";
    }

    void ShowWinUI()
    {
        if (_gameEnded)
            return;

        _gameEnded = true;
        SpawnEndUI(winUI);
    }

    void ShowLoseUI()
    {
        if (_gameEnded)
            return;

        _gameEnded = true;
        SpawnEndUI(loseUI);
    }

    void SpawnEndUI(GameObject prefab)
    {
        if (prefab == null)
            return;

        Transform parent = _uiParent != null ? _uiParent : transform;
        Instantiate(prefab, parent, false);
    }

    /// <summary>
    /// Next Level: tăng level (vòng lặp config vô hạn), Save rồi reload scene.
    /// </summary>
    public void GoToNextLevel()
    {
        if (_userConfig == null)
            return;

        _userConfig.level++;
        _userConfig.SyncLevelFields();
        _userConfig.Save();
        ReloadActiveScene();
    }

    /// <summary>
    /// Play Again: reload lại đúng level hiện tại.
    /// </summary>
    public void ReloadLevel()
    {
        if (_userConfig != null)
        {
            _userConfig.SyncLevelFields();
            _userConfig.Save();
        }

        ReloadActiveScene();
    }

    static void ReloadActiveScene()
    {
        Scene active = SceneManager.GetActiveScene();
        SceneManager.LoadScene(active.buildIndex);
    }

#if UNITY_EDITOR
    void Reset()
    {
        if (_userConfig == null)
            _userConfig = AssetDatabase.LoadAssetAtPath<UserConfig>(UserConfigAssetPath);

        if (_levelManager == null)
            _levelManager = GetComponent<LevelManager>();
    }

    void OnValidate()
    {
        if (_userConfig == null)
            _userConfig = AssetDatabase.LoadAssetAtPath<UserConfig>(UserConfigAssetPath);

        if (_levelManager == null)
            _levelManager = GetComponent<LevelManager>();
    }
#endif
}
