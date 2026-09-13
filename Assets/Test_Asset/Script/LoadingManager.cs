using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// LoadingScene: updates fillAmount then loads Gameplay when full.
/// </summary>
public class LoadingManager : MonoBehaviour
{
    public Image loadingBarImage;
    [SerializeField] SoundConfig _soundConfig;

    const string GameplaySceneName = "Gameplay";
    /// <summary>Minimum duration in seconds so the bar does not fill too quickly.</summary>
    const float MinLoadDuration = 3.5f;
#if UNITY_EDITOR
    const string SoundConfigAssetPath = "Assets/Test_Asset/Config/SoundConfig.asset";
#endif

    void Awake()
    {
        if (_soundConfig != null)
            SoundManager.Ensure(_soundConfig).PlayBgm(SoundConfig.BgmLoading);
    }

    void Start()
    {
        _ = RunLoadingAsync();
    }

    async Awaitable RunLoadingAsync()
    {
        if (loadingBarImage != null)
        {
            loadingBarImage.type = Image.Type.Filled;
            loadingBarImage.fillMethod = Image.FillMethod.Horizontal;
            loadingBarImage.fillAmount = 0f;
        }

        AsyncOperation loadOp = SceneManager.LoadSceneAsync(GameplaySceneName);
        if (loadOp == null)
        {
            Debug.LogError("Failed to load the Gameplay scene. Check Build Settings.");
            return;
        }

        loadOp.allowSceneActivation = false;

        float elapsed = 0f;
        float displayFill = 0f;

        while (!loadOp.isDone)
        {
            elapsed += Time.deltaTime;

            // AsyncOperation.progress stops at 0.9 until the scene is activated
            float loadProgress = Mathf.Clamp01(loadOp.progress / 0.9f);
            float timeProgress = Mathf.Clamp01(elapsed / MinLoadDuration);
            float targetFill = Mathf.Min(loadProgress, timeProgress);

            // Smooth fillAmount to avoid sudden jumps
            displayFill = Mathf.MoveTowards(displayFill, targetFill, Time.deltaTime * 0.45f);

            if (loadingBarImage != null)
                loadingBarImage.fillAmount = displayFill;

            if (loadProgress >= 1f && timeProgress >= 1f && displayFill >= 0.99f)
            {
                if (loadingBarImage != null)
                    loadingBarImage.fillAmount = 1f;

                loadOp.allowSceneActivation = true;
            }

            await Awaitable.NextFrameAsync();
        }
    }

#if UNITY_EDITOR
    void Reset()
    {
        LoadSoundConfig();
    }

    void OnValidate()
    {
        if (_soundConfig == null)
            LoadSoundConfig();
    }

    void LoadSoundConfig()
    {
        _soundConfig = AssetDatabase.LoadAssetAtPath<SoundConfig>(SoundConfigAssetPath);
    }
#endif
}
