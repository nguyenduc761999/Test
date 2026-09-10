using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// LoadingScene: cập nhật fillAmount rồi chuyển sang Gameplay khi đầy.
/// </summary>
public class LoadingManager : MonoBehaviour
{
    public Image loadingBarImage;

    const string GameplaySceneName = "Gameplay";
    /// <summary>Thời gian tối thiểu (giây) để thanh không đầy quá nhanh.</summary>
    const float MinLoadDuration = 3.5f;

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
            Debug.LogError("Không load được scene Gameplay. Kiểm tra Build Settings.");
            return;
        }

        loadOp.allowSceneActivation = false;

        float elapsed = 0f;
        float displayFill = 0f;

        while (!loadOp.isDone)
        {
            elapsed += Time.deltaTime;

            // AsyncOperation.progress dừng ở 0.9 trước khi activate
            float loadProgress = Mathf.Clamp01(loadOp.progress / 0.9f);
            float timeProgress = Mathf.Clamp01(elapsed / MinLoadDuration);
            float targetFill = Mathf.Min(loadProgress, timeProgress);

            // Làm mượt fillAmount, tránh nhảy đột ngột
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
}
