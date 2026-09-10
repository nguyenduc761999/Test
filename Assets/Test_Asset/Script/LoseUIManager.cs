using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI thua: Play Again reload lại level hiện tại.
/// </summary>
public class LoseUIManager : MonoBehaviour
{
    [SerializeField] Button playAgainBtn;

    GameplayManager _gameplayManager;

    void Awake()
    {
        _gameplayManager = FindFirstObjectByType<GameplayManager>();

        if (playAgainBtn != null)
            playAgainBtn.onClick.AddListener(OnPlayAgainClicked);
    }

    void OnDestroy()
    {
        if (playAgainBtn != null)
            playAgainBtn.onClick.RemoveListener(OnPlayAgainClicked);
    }

    void OnPlayAgainClicked()
    {
        _gameplayManager?.ReloadLevel();
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
        if (playAgainBtn != null)
            return;

        Transform btn = transform.Find("PlayAgainBtn");
        if (btn != null)
            playAgainBtn = btn.GetComponent<Button>();
    }
#endif
}
