using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI máu Player: Fill Amount + ghost drain + rung healthBG khi nhận damage.
/// </summary>
public class UserInfoUIManager : MonoBehaviour
{
    [SerializeField] Image healthBar;
    [SerializeField] Image healthBG;
    [SerializeField] Image ghostHealth;
    [SerializeField] PlayerController playerController;

    [SerializeField] float ghostLerpSpeed = 1.5f;
    [SerializeField] float shakeDuration = 0.25f;
    [SerializeField] float shakeStrength = 10f;

    RectTransform _healthBgRect;
    Vector2 _healthBgOrigin;
    float _ghostFill = 1f;
    float _targetFill = 1f;
    int _ghostToken;
    int _shakeToken;

    void Awake()
    {
        // Tìm Player (prefab instance trên scene) nếu chưa gán
        if (playerController == null)
            playerController = FindFirstObjectByType<PlayerController>();

        _healthBgRect = healthBG != null ? healthBG.rectTransform : null;
        if (_healthBgRect != null)
            _healthBgOrigin = _healthBgRect.anchoredPosition;

        if (playerController != null)
            playerController.HealthChanged += OnPlayerHealthChanged;
    }

    void Start()
    {
        // Sync sau Awake của PlayerController (đã Load UserConfig.health)
        ApplyFill(GetHealthFill(), true);
    }

    void OnDestroy()
    {
        if (playerController != null)
            playerController.HealthChanged -= OnPlayerHealthChanged;
    }

    void OnPlayerHealthChanged()
    {
        ApplyFill(GetHealthFill(), false);
        ShakeHealthBgAsync();
    }

    /// <summary>
    /// Tỉ lệ máu hiện tại / max từ PlayerController.
    /// </summary>
    float GetHealthFill()
    {
        if (playerController == null)
            return 0f;

        int maxHealth = playerController.MaxHealth;
        if (maxHealth <= 0)
            return 0f;

        return Mathf.Clamp01((float)playerController.Health / maxHealth);
    }

    /// <summary>
    /// Cập nhật healthBar ngay; ghostHealth giảm trễ nếu không instant.
    /// </summary>
    void ApplyFill(float fill, bool instantGhost)
    {
        _targetFill = fill;

        if (healthBar != null)
            healthBar.fillAmount = fill;

        if (instantGhost)
        {
            _ghostToken++;
            _ghostFill = fill;
            if (ghostHealth != null)
                ghostHealth.fillAmount = fill;
            return;
        }

        DrainGhostAsync();
    }

    /// <summary>
    /// Ghost fillAmount giảm dần theo healthBar.
    /// </summary>
    async Awaitable DrainGhostAsync()
    {
        int token = ++_ghostToken;
        while (ghostHealth != null && _ghostFill > _targetFill + 0.001f)
        {
            if (token != _ghostToken || this == null)
                return;

            _ghostFill = Mathf.MoveTowards(_ghostFill, _targetFill, ghostLerpSpeed * Time.deltaTime);
            ghostHealth.fillAmount = _ghostFill;
            await Awaitable.NextFrameAsync();
        }

        if (token != _ghostToken || this == null || ghostHealth == null)
            return;

        _ghostFill = _targetFill;
        ghostHealth.fillAmount = _ghostFill;
    }

    /// <summary>
    /// Rung chấn healthBG khi bị đánh.
    /// </summary>
    async Awaitable ShakeHealthBgAsync()
    {
        if (_healthBgRect == null)
            return;

        int token = ++_shakeToken;
        float elapsed = 0f;

        while (elapsed < shakeDuration)
        {
            if (token != _shakeToken || this == null || _healthBgRect == null)
                return;

            elapsed += Time.deltaTime;
            float damp = 1f - Mathf.Clamp01(elapsed / shakeDuration);
            Vector2 offset = Random.insideUnitCircle * (shakeStrength * damp);
            _healthBgRect.anchoredPosition = _healthBgOrigin + offset;
            await Awaitable.NextFrameAsync();
        }

        if (token != _shakeToken || this == null || _healthBgRect == null)
            return;

        _healthBgRect.anchoredPosition = _healthBgOrigin;
    }

#if UNITY_EDITOR
    void Reset()
    {
        TryAutoBindImages();
    }

    void OnValidate()
    {
        TryAutoBindImages();
    }

    /// <summary>
    /// Tự gắn HeathBar / HeathBG / GhostHealth trong hierarchy.
    /// </summary>
    void TryAutoBindImages()
    {
        if (healthBar == null)
        {
            Transform bar = transform.Find("HeathBG/HeathBar");
            if (bar == null)
                bar = FindChildByName(transform, "HeathBar");
            if (bar != null)
                healthBar = bar.GetComponent<Image>();
        }

        if (healthBG == null)
        {
            Transform bg = transform.Find("HeathBG");
            if (bg == null)
                bg = FindChildByName(transform, "HeathBG");
            if (bg != null)
                healthBG = bg.GetComponent<Image>();
        }

        if (ghostHealth == null)
        {
            Transform ghost = transform.Find("HeathBG/GhostHealth");
            if (ghost == null)
                ghost = FindChildByName(transform, "GhostHealth");
            if (ghost != null)
                ghostHealth = ghost.GetComponent<Image>();
        }
    }

    static Transform FindChildByName(Transform root, string targetName)
    {
        if (root == null)
            return null;

        if (root.name == targetName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildByName(root.GetChild(i), targetName);
            if (found != null)
                return found;
        }

        return null;
    }
#endif
}
