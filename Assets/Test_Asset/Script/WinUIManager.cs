using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Win UI: Next Level advances to the following level.
/// </summary>
public class WinUIManager : MonoBehaviour
{
    [SerializeField] Button nextLevelBtn;
    [SerializeField] SoundConfig _soundConfig;

    GameplayManager _gameplayManager;
    AudioSource _clickSource;

#if UNITY_EDITOR
    const string SoundConfigAssetPath = "Assets/Test_Asset/Config/SoundConfig.asset";
#endif

    void Awake()
    {
        _gameplayManager = FindFirstObjectByType<GameplayManager>();
        BindNextLevelBtn();
        SetupClickSource();

        if (_soundConfig != null)
            SoundManager.Ensure(_soundConfig);

        if (nextLevelBtn != null)
            nextLevelBtn.onClick.AddListener(OnNextLevelClicked);
    }

    void BindNextLevelBtn()
    {
        if (nextLevelBtn != null)
            return;

        Transform btn = transform.Find("NextLevelBtn");
        if (btn != null)
            nextLevelBtn = btn.GetComponent<Button>();
    }

    void SetupClickSource()
    {
        _clickSource = gameObject.GetComponent<AudioSource>();
        if (_clickSource == null)
            _clickSource = gameObject.AddComponent<AudioSource>();

        _clickSource.playOnAwake = false;
        _clickSource.loop = false;
        _clickSource.spatialBlend = 0f;
        _clickSource.ignoreListenerPause = true;
        _clickSource.priority = 0;
        _clickSource.volume = 1f;
        _clickSource.mute = false;
        _clickSource.dopplerLevel = 0f;
    }

    void OnDestroy()
    {
        if (nextLevelBtn != null)
            nextLevelBtn.onClick.RemoveListener(OnNextLevelClicked);
    }

    async void OnNextLevelClicked()
    {
        PlayClick();
        await Awaitable.WaitForSecondsAsync(0.5f);
        _gameplayManager?.GoToNextLevel();
    }

    void PlayClick()
    {
        if (_soundConfig != null)
            SoundManager.Ensure(_soundConfig);

        SoundManager.Instance?.PlayButton(SoundConfig.ButtonClick);

        SoundEntry entry = _soundConfig != null ? _soundConfig.FindButtonByName(SoundConfig.ButtonClick) : null;
        if (entry == null || entry.Clip == null)
            entry = _soundConfig != null ? _soundConfig.FindSfxByName(SoundConfig.ButtonClick) : null;
        if (entry == null || entry.Clip == null || _clickSource == null)
            return;

        _clickSource.clip = entry.Clip;
        _clickSource.Play();
    }

#if UNITY_EDITOR
    void Reset()
    {
        TryAutoBind();
    }

    void OnValidate()
    {
        TryAutoBind();
    }

    void TryAutoBind()
    {
        if (nextLevelBtn == null)
        {
            Transform btn = transform.Find("NextLevelBtn");
            if (btn != null)
                nextLevelBtn = btn.GetComponent<Button>();
        }

        if (_soundConfig == null)
            _soundConfig = AssetDatabase.LoadAssetAtPath<SoundConfig>(SoundConfigAssetPath);
    }
#endif
}
