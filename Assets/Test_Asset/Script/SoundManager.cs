using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Plays BGM / SFX / Button from SoundConfig. Survives scene loads.
/// </summary>
[DefaultExecutionOrder(-200)]
public class SoundManager : MonoBehaviour
{
    [SerializeField] SoundConfig _soundConfig;

    AudioSource _bgmSource;
    AudioSource _sfxSource;
    AudioSource _buttonSource;

    static SoundManager _instance;

#if UNITY_EDITOR
    const string SoundConfigAssetPath = "Assets/Test_Asset/Config/SoundConfig.asset";
#endif

    public static SoundManager Instance => _instance;

    /// <summary>
    /// Creates or gets SoundManager and assigns SoundConfig.
    /// </summary>
    public static SoundManager Ensure(SoundConfig config)
    {
        if (_instance == null)
        {
            SoundManager found = FindFirstObjectByType<SoundManager>();
            if (found != null)
                _instance = found;
            else
            {
                GameObject go = new GameObject("SoundManager");
                _instance = go.AddComponent<SoundManager>();
            }
        }

        if (config != null)
            _instance._soundConfig = config;

        _instance.SetupSources();
        return _instance;
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            if (_soundConfig != null)
                _instance._soundConfig = _soundConfig;
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        AudioListener.volume = 1f;
        SetupSources();
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    void SetupSources()
    {
        if (_bgmSource == null)
        {
            _bgmSource = gameObject.AddComponent<AudioSource>();
            _bgmSource.playOnAwake = false;
            _bgmSource.loop = true;
            _bgmSource.spatialBlend = 0f;
            _bgmSource.priority = 32;
            _bgmSource.mute = false;
            _bgmSource.ignoreListenerPause = true;
            _bgmSource.dopplerLevel = 0f;
        }

        if (_sfxSource == null)
        {
            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;
            _sfxSource.loop = false;
            _sfxSource.spatialBlend = 0f;
            _sfxSource.priority = 64;
            _sfxSource.ignoreListenerPause = true;
        }

        if (_buttonSource == null)
        {
            _buttonSource = gameObject.AddComponent<AudioSource>();
            _buttonSource.playOnAwake = false;
            _buttonSource.loop = false;
            _buttonSource.spatialBlend = 0f;
            _buttonSource.priority = 0;
            _buttonSource.mute = false;
            _buttonSource.ignoreListenerPause = true;
            _buttonSource.dopplerLevel = 0f;
            _buttonSource.volume = 1f;
        }
    }

    /// <summary>
    /// Plays looping BGM by name from SoundConfig.
    /// </summary>
    public void PlayBgm(string soundName)
    {
        SoundEntry entry = FindEntry(SoundCategory.BGM, soundName);
        if (entry == null || entry.Clip == null || _bgmSource == null)
            return;

        _bgmSource.mute = false;
        _bgmSource.enabled = true;
        _bgmSource.loop = true;
        _bgmSource.spatialBlend = 0f;
        _bgmSource.volume = entry.Volume;

        if (_bgmSource.clip == entry.Clip && _bgmSource.isPlaying)
            return;

        _bgmSource.clip = entry.Clip;
        _bgmSource.Play();
    }

    /// <summary>
    /// Stops the currently playing BGM.
    /// </summary>
    public void StopBgm()
    {
        if (_bgmSource != null && _bgmSource.isPlaying)
            _bgmSource.Stop();
    }

    /// <summary>
    /// Plays a 2D SFX by name.
    /// </summary>
    public void PlaySfx(string soundName)
    {
        PlayOneShot(FindEntry(SoundCategory.SFX, soundName));
    }

    /// <summary>
    /// Plays SFX at a world position — mobile uses 2D so it is always audible.
    /// </summary>
    public void PlaySfxAt(string soundName, Vector3 position)
    {
        _ = position;
        PlaySfx(soundName);
    }

    /// <summary>
    /// Plays a UI button sound — dedicated 2D channel.
    /// </summary>
    public void PlayButton(string soundName)
    {
        SetupSources();
        SoundEntry entry = FindEntry(SoundCategory.Button, soundName);
        if (entry == null || entry.Clip == null)
            entry = FindEntry(SoundCategory.SFX, soundName);
        if (entry == null || entry.Clip == null || _buttonSource == null)
            return;

        AudioClip clip = entry.Clip;
        if (clip.loadState != AudioDataLoadState.Loaded)
            clip.LoadAudioData();

        _buttonSource.enabled = true;
        _buttonSource.mute = false;
        _buttonSource.spatialBlend = 0f;
        _buttonSource.ignoreListenerPause = true;
        _buttonSource.volume = 1f;
        _buttonSource.PlayOneShot(clip, 1f);
    }

    void PlayOneShot(SoundEntry entry)
    {
        if (entry == null || entry.Clip == null || _sfxSource == null)
            return;

        _sfxSource.PlayOneShot(entry.Clip, entry.Volume);
    }

    SoundEntry FindEntry(SoundCategory category, string soundName)
    {
        if (_soundConfig == null)
            return null;

        if (category == SoundCategory.SFX)
            return _soundConfig.FindSfxByName(soundName);
        if (category == SoundCategory.BGM)
            return _soundConfig.FindBgmByName(soundName);
        if (category == SoundCategory.Button)
            return _soundConfig.FindButtonByName(soundName);

        return null;
    }

#if UNITY_EDITOR
    void Reset()
    {
        LoadConfig();
    }

    void OnValidate()
    {
        if (_soundConfig == null)
            LoadConfig();
    }

    void LoadConfig()
    {
        _soundConfig = AssetDatabase.LoadAssetAtPath<SoundConfig>(SoundConfigAssetPath);
    }
#endif
}
