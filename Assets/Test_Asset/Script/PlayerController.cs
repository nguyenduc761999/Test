using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class PlayerController : MonoBehaviour
{
    GameObject _characterModel;
    FixedJoystick _fixedJoystick;
    Animator _animController;
    AudioSource _audioSource;

    [SerializeField] float _rangeAttack = 3f;
    [SerializeField] SoundConfig _soundConfig;
    [SerializeField, SfxSound] string fireSound;

    const float MoveSpeed = 5f;
    const float RotateSpeed = 10f;
    const int EnemyHitBufferSize = 16;
    const string ShootAnimStateName = "infantry_combat_shoot";
#if UNITY_EDITOR
    const string SoundConfigAssetPath = "Assets/Test_Asset/Config/SoundConfig.asset";
#endif

    LayerMask _enemyLayerMask;
    Collider[] _enemyHits;
    int _upperBodyLayerIndex = -1;
    bool _shootAnimActive;
    int _lastShootCycleIndex = -1;

    void Awake()
    {
        // Lấy model con của Character khi vào Play
        Transform character = transform.Find("Character");
        if (character != null && character.childCount > 0)
            _characterModel = character.GetChild(0).gameObject;

        // Cache Animator của characterModel
        if (_characterModel != null)
        {
            _animController = _characterModel.GetComponent<Animator>();
            if (_animController != null)
                _upperBodyLayerIndex = _animController.GetLayerIndex("UpperBody");
        }

        // Cache FixedJoystick trên scene
        _fixedJoystick = FindFirstObjectByType<FixedJoystick>();

        _enemyLayerMask = LayerMask.GetMask("Enemy");
        _enemyHits = new Collider[EnemyHitBufferSize];

        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();
    }

    void Update()
    {
        if (_characterModel == null)
            return;

        float horizontal = 0f;
        float vertical = 0f;
        bool isRunning = false;
        if (_fixedJoystick != null)
        {
            horizontal = _fixedJoystick.Horizontal;
            vertical = _fixedJoystick.Vertical;
            isRunning = horizontal != 0f || vertical != 0f;
        }

        // Bật/tắt bool Run theo trạng thái joystick
        if (_animController != null)
            _animController.SetBool("Run", isRunning);

        // Tìm Enemy gần nhất trong rangeAttack
        Transform nearestEnemy = FindNearestEnemy();
        bool hasEnemy = nearestEnemy != null;

        // UpperBody: có Enemy trong range thì Weight = 1, ngược lại = 0
        if (_animController != null && _upperBodyLayerIndex >= 0)
            _animController.SetLayerWeight(_upperBodyLayerIndex, hasEnemy ? 1f : 0f);

        // Phát SFX 1 lần mỗi khi bắt đầu chu kỳ anim shoot
        TryPlayFireSoundOnShootStart();

        // Có Enemy trong range: khóa cứng hướng nhìn, không xoay theo di chuyển
        if (hasEnemy)
        {
            Vector3 lookDirection = nearestEnemy.position - _characterModel.transform.position;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude > 0.0001f)
                _characterModel.transform.rotation = Quaternion.LookRotation(lookDirection);
        }
        // Hết Enemy trong range mới xoay theo hướng di chuyển
        else if (isRunning)
        {
            Vector3 lookDirection = new Vector3(horizontal, 0f, vertical);
            Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
            _characterModel.transform.rotation = Quaternion.Slerp(
                _characterModel.transform.rotation,
                targetRotation,
                RotateSpeed * Time.deltaTime);
        }

        if (isRunning)
            transform.Translate(new Vector3(horizontal, 0f, vertical) * MoveSpeed * Time.deltaTime, Space.World);
    }

    /// <summary>
    /// Khi UpperBody bắt đầu (hoặc lặp lại) state shoot thì phát fireSound một lần.
    /// </summary>
    void TryPlayFireSoundOnShootStart()
    {
        if (_animController == null || _upperBodyLayerIndex < 0)
            return;

        float layerWeight = _animController.GetLayerWeight(_upperBodyLayerIndex);
        if (layerWeight <= 0f)
        {
            _shootAnimActive = false;
            return;
        }

        AnimatorStateInfo stateInfo = _animController.GetCurrentAnimatorStateInfo(_upperBodyLayerIndex);
        if (!stateInfo.IsName(ShootAnimStateName))
        {
            _shootAnimActive = false;
            return;
        }

        int cycleIndex = Mathf.FloorToInt(stateInfo.normalizedTime);
        if (!_shootAnimActive || cycleIndex != _lastShootCycleIndex)
        {
            PlayFireSound();
            _lastShootCycleIndex = cycleIndex;
        }

        _shootAnimActive = true;
    }

    void PlayFireSound()
    {
        if (_soundConfig == null || _audioSource == null || string.IsNullOrEmpty(fireSound))
            return;

        SoundEntry entry = _soundConfig.FindSfxByName(fireSound);
        if (entry == null || entry.Clip == null)
            return;

        _audioSource.PlayOneShot(entry.Clip, entry.Volume);
    }

#if UNITY_EDITOR
    void Reset()
    {
        // Gán sẵn SoundConfig mặc định khi gắn component
        _soundConfig = AssetDatabase.LoadAssetAtPath<SoundConfig>(SoundConfigAssetPath);
    }

    void OnValidate()
    {
        if (_soundConfig == null)
            _soundConfig = AssetDatabase.LoadAssetAtPath<SoundConfig>(SoundConfigAssetPath);
    }
#endif

    Transform FindNearestEnemy()
    {
        if (_enemyHits == null)
            return null;

        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            _rangeAttack,
            _enemyHits,
            _enemyLayerMask);

        Transform nearest = null;
        float nearestDistSq = float.MaxValue;
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = _enemyHits[i];
            if (hit == null)
                continue;

            float distSq = (hit.transform.position - transform.position).sqrMagnitude;
            if (distSq < nearestDistSq)
            {
                nearestDistSq = distSq;
                nearest = hit.transform;
            }
        }

        return nearest;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // Kéo gizmo hình cầu để chỉnh rangeAttack trên Scene
        Handles.color = new Color(1f, 0.3f, 0.2f, 0.9f);
        float newRange = Handles.RadiusHandle(Quaternion.identity, transform.position, Mathf.Max(0f, _rangeAttack));
        if (!Mathf.Approximately(newRange, _rangeAttack))
        {
            Undo.RecordObject(this, "Chỉnh Range Attack");
            _rangeAttack = Mathf.Max(0f, newRange);
            EditorUtility.SetDirty(this);
        }

        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.15f);
        Gizmos.DrawSphere(transform.position, _rangeAttack);
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, _rangeAttack);
    }
#endif
}
