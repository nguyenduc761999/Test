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
    [SerializeField] SoundConfig _soundConfig;
    [SerializeField] GameObject _level1Prefab;
    [SerializeField] GameObject _level2Prefab;

    float _timeSpawnInterval;
    float _timeSpawnCountdown;
    float _playTimeCountdown;
    bool _gameEnded;

    PlayerController _playerController;
    Transform _uiParent;
    Transform _controlPanel;
    Transform _userInfoUi;

#if UNITY_EDITOR
    const string UserConfigAssetPath = "Assets/Test_Asset/Config/UserConfig.asset";
    const string SoundConfigAssetPath = "Assets/Test_Asset/Config/SoundConfig.asset";
    const string Level1PrefabPath = "Assets/Test_Asset/Prefabs/Level_1.prefab";
    const string Level2PrefabPath = "Assets/Test_Asset/Prefabs/Level_2.prefab";
#endif

    void Awake()
    {
        // Cache Player + Canvas (do not Find in Update)
        _playerController = FindFirstObjectByType<PlayerController>();
        Canvas canvas = FindFirstObjectByType<Canvas>();
        _uiParent = canvas != null ? canvas.transform : null;
        if (_uiParent != null)
        {
            _controlPanel = _uiParent.Find("ControlPanel");
            _userInfoUi = _uiParent.Find("UserInfoUI");
        }

        if (_userConfig != null)
        {
            _userConfig.Load();
            _userConfig.SyncLevelFields();
        }

        // Even levels (2, 4, ...) use the sloped map
        ApplyActiveMap();

        if (_soundConfig != null)
            SoundManager.Ensure(_soundConfig).PlayBgm(SoundConfig.BgmGameplay);

        RefreshSpawnIntervalFromLevel();
        _timeSpawnCountdown = _timeSpawnInterval;

        // playTime comes from LevelConfig for the user's current level
        LevelEntry level = GetCurrentLevel();
        _playTimeCountdown = level != null ? level.PlayTime : 0f;

        RefreshLevelTxt();
        RefreshPlayTimeTxt();
    }

    void Update()
    {
        if (_gameEnded)
            return;

        // Player died before time ran out → LoseUI
        if (_playerController != null && _playerController.IsDead)
        {
            ShowLoseUI();
            return;
        }

        TickPlayTime();
        TickZombieSpawn();
    }

    /// <summary>
    /// Counts down playTime; at 0 with Player still alive → WinUI.
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

        // Countdown then spawn at a random location + random zombie prefab from the level list
        _timeSpawnCountdown -= Time.deltaTime;
        if (_timeSpawnCountdown > 0f)
            return;

        SpawnZombies(level);
        _timeSpawnCountdown = _timeSpawnInterval;
    }

    /// <summary>
    /// Reads timeSpawnZombie from the current level (mapped by UserConfig.level).
    /// </summary>
    void RefreshSpawnIntervalFromLevel()
    {
        LevelEntry level = GetCurrentLevel();
        _timeSpawnInterval = level != null ? level.TimeSpawnZombie : 0f;
    }

    /// <summary>
    /// Enables the flat or sloped map by level; binds spawn points and places the Player.
    /// </summary>
    void ApplyActiveMap()
    {
        bool useSlope = _userConfig != null && ((_userConfig.level - 1) & 1) == 1;
        string activeName = useSlope ? "Level_2" : "Level_1";
        string idleName = useSlope ? "Level_1" : "Level_2";

        GameObject idle = FindSceneRoot(idleName);
        if (idle != null)
            idle.SetActive(false);

        GameObject active = FindSceneRoot(activeName);
        if (active == null)
        {
            GameObject prefab = useSlope ? _level2Prefab : _level1Prefab;
            if (prefab == null)
                return;

            active = Instantiate(prefab);
            active.name = activeName;
        }
        else
            active.SetActive(true);

        BindSpawnLocations(active.transform);
        WarpPlayerToMap(active.transform);
    }

    /// <summary>
    /// Copies the active map's LocationList into locationSpawnZomebie.
    /// </summary>
    void BindSpawnLocations(Transform root)
    {
        if (locationSpawnZomebie == null)
            locationSpawnZomebie = new List<Transform>();
        locationSpawnZomebie.Clear();
        if (root == null)
            return;

        Transform list = root.Find("LocationList");
        if (list == null)
            return;

        for (int i = 0; i < list.childCount; i++)
        {
            Transform child = list.GetChild(i);
            if (child != null)
                locationSpawnZomebie.Add(child);
        }
    }

    /// <summary>
    /// Places the Player at PlayerSpawn on the active map.
    /// </summary>
    void WarpPlayerToMap(Transform root)
    {
        if (root == null || _playerController == null)
            return;

        Transform spawn = root.Find("PlayerSpawn");
        if (spawn == null)
            return;

        CharacterController cc = _playerController.GetComponent<CharacterController>();
        if (cc != null)
            cc.enabled = false;

        _playerController.transform.SetPositionAndRotation(spawn.position, spawn.rotation);

        if (cc != null)
            cc.enabled = true;
    }

    /// <summary>
    /// Finds a scene root by name — including inactive objects.
    /// </summary>
    GameObject FindSceneRoot(string objectName)
    {
        GameObject[] roots = gameObject.scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            if (root != null && root.name == objectName)
                return root;
        }

        return null;
    }

    /// <summary>
    /// Level config for the user level (wraps when past the last LevelConfig entry).
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

        // Pick a random location from the list to spawn
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

        // Display mm:ss
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
        _playerController?.LockMovement();
        KillRemainingZombies();
        SoundManager.Instance?.StopBgm();
        SpawnEndUI(winUI);
    }

    void ShowLoseUI()
    {
        if (_gameEnded)
            return;

        _gameEnded = true;
        FreezeRemainingZombies();
        SoundManager.Instance?.StopBgm();
        SpawnEndUI(loseUI);
    }

    /// <summary>
    /// Win: remaining zombies on the field ragdoll-die.
    /// </summary>
    void KillRemainingZombies()
    {
        EnemyController[] enemies = FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyController enemy = enemies[i];
            if (enemy != null)
                enemy.ApplyDie();
        }
    }

    /// <summary>
    /// Lose: zombies stand still and stop chasing.
    /// </summary>
    void FreezeRemainingZombies()
    {
        EnemyController[] enemies = FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyController enemy = enemies[i];
            if (enemy != null)
                enemy.FreezeInPlace();
        }
    }

    void SpawnEndUI(GameObject prefab)
    {
        if (prefab == null)
            return;

        HideGameplayHud();

        Transform parent = _uiParent != null ? _uiParent : transform;
        Instantiate(prefab, parent, false);
    }

    /// <summary>
    /// Hides in-match HUD when Win/Lose is shown.
    /// </summary>
    void HideGameplayHud()
    {
        HideHudNode(playTime);
        HideHudNode(levelTxt);
        if (_controlPanel != null)
            _controlPanel.gameObject.SetActive(false);
        if (_userInfoUi != null)
            _userInfoUi.gameObject.SetActive(false);
    }

    /// <summary>
    /// Hides the parent plaque if the text sits inside a HUD frame.
    /// </summary>
    void HideHudNode(TMP_Text txt)
    {
        if (txt == null)
            return;

        Transform node = txt.transform;
        if (node.parent != null && node.parent != _uiParent)
            node = node.parent;

        node.gameObject.SetActive(false);
    }

    /// <summary>
    /// Next Level: increment level (config wraps forever), Save, then reload the scene.
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
    /// Play Again: reload the current level.
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
        if (_soundConfig == null)
            _soundConfig = AssetDatabase.LoadAssetAtPath<SoundConfig>(SoundConfigAssetPath);

        if (_levelManager == null)
            _levelManager = GetComponent<LevelManager>();
        if (_level1Prefab == null)
            _level1Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Level1PrefabPath);
        if (_level2Prefab == null)
            _level2Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Level2PrefabPath);
    }

    void OnValidate()
    {
        if (_userConfig == null)
            _userConfig = AssetDatabase.LoadAssetAtPath<UserConfig>(UserConfigAssetPath);
        if (_soundConfig == null)
            _soundConfig = AssetDatabase.LoadAssetAtPath<SoundConfig>(SoundConfigAssetPath);

        if (_levelManager == null)
            _levelManager = GetComponent<LevelManager>();
        if (_level1Prefab == null)
            _level1Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Level1PrefabPath);
        if (_level2Prefab == null)
            _level2Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Level2PrefabPath);
    }
#endif
}
