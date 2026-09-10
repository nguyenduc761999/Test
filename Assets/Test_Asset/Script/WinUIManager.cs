using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI thắng: Next Level chuyển sang level tiếp theo.
/// </summary>
public class WinUIManager : MonoBehaviour
{
    [SerializeField] Button nextLevelBtn;

    GameplayManager _gameplayManager;

    void Awake()
    {
        _gameplayManager = FindFirstObjectByType<GameplayManager>();

        if (nextLevelBtn != null)
            nextLevelBtn.onClick.AddListener(OnNextLevelClicked);
    }

    void OnDestroy()
    {
        if (nextLevelBtn != null)
            nextLevelBtn.onClick.RemoveListener(OnNextLevelClicked);
    }

    void OnNextLevelClicked()
    {
        _gameplayManager?.GoToNextLevel();
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
        if (nextLevelBtn != null)
            return;

        Transform btn = transform.Find("NextLevelBtn");
        if (btn != null)
            nextLevelBtn = btn.GetComponent<Button>();
    }
#endif
}
