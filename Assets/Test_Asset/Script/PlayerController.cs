using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] SoundConfig _soundConfig;
    [SerializeField] UserConfig _userConfig;
    [SerializeField] WeaponConfig _weaponConfig;

    GameObject _characterModel;
    FixedJoystick _fixedJoystick;
    Animator _animController;
    AudioSource _audioSource;
    CharacterController _characterController;

    [SerializeField] float _rangeAttack = 10f;
    [SerializeField] float rangeGrenade = 12f;

    [SerializeField] Transform gunLocation;
    Transform fireLocation;

    WeaponEntry _currentWeapon;

    /// <summary>Current health — initialized from UserConfig, reduced on damage.</summary>
    [SerializeField] int health;

    /// <summary>VFX when hit by a Zombie — spawn at the Player position, rotated opposite the attack direction.</summary>
    [SerializeField] GameObject hitVFX;

    /// <summary>Max health at start (after Load UserConfig).</summary>
    int _maxHealth = 1;

    /// <summary>Current Player health.</summary>
    public int Health => health;

    /// <summary>Max health used by UI Fill Amount.</summary>
    public int MaxHealth => _maxHealth;

    /// <summary>Fired when health changes (damage / death).</summary>
    public event System.Action HealthChanged;

    [SerializeField] float _ragdollWaitSeconds = 2f;
    [SerializeField] float _dissolveDuration = 1.5f;
    [SerializeField] Shader _dissolveShader;

    const float MoveSpeed = 5f;
    const float RotateSpeed = 10f;
    const float GroundStickVelocity = -2f;
    const int EnemyHitBufferSize = 16;
    const string ShootAnimStateName = "infantry_combat_shoot";
    const string FireSpeedAnimParam = "FireSpeed";
    const string FireLocationName = "FireLocation";
    const string PlaneName = "Plane";
    const float GrenadeSpawnHeight = 1.2f;
    const int AimRingSegments = 48;
#if UNITY_EDITOR
    const string SoundConfigAssetPath = "Assets/Test_Asset/Config/SoundConfig.asset";
    const string UserConfigAssetPath = "Assets/Test_Asset/Config/UserConfig.asset";
    const string WeaponConfigAssetPath = "Assets/Test_Asset/Config/WeaponConfig.asset";
#endif

    static readonly int DissolveAmountId = Shader.PropertyToID("_DissolveAmount");
    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    LayerMask _enemyLayerMask;
    Collider[] _enemyHits;
    int _upperBodyLayerIndex = -1;
    bool _shootAnimActive;
    int _lastShootCycleIndex = -1;

    Rigidbody[] _ragdollBodies;
    Collider[] _ragdollColliders;
    Collider[] _rootColliders;
    Renderer[] _renderers;
    Material[] _dissolveMaterials;
    bool _isDead;
    bool _movementLocked;
    float _verticalVelocity;

    Camera _mainCamera;
    bool _grenadeAiming;
    Vector3 _grenadeAimPoint;
    float _cachedRangeExplosion = 2.5f;
    LineRenderer _maxRangeRing;
    LineRenderer _aoeRing;
    RaycastHit[] _groundHits;

    /// <summary>Player is dead (health &lt;= 0).</summary>
    public bool IsDead => _isDead;

    void Awake()
    {
        // Cache CharacterController — Move() collides with colliders (Translate goes through walls)
        _characterController = GetComponent<CharacterController>();
        if (_characterController == null)
            _characterController = gameObject.AddComponent<CharacterController>();

        _characterController.height = 1.8f;
        _characterController.radius = 0.35f;
        _characterController.center = new Vector3(0f, 0.9f, 0f);
        _characterController.skinWidth = 0.08f;
        _characterController.minMoveDistance = 0f;

        // Get the Character child model when entering Play
        Transform character = transform.Find("Character");
        if (character != null && character.childCount > 0)
            _characterModel = character.GetChild(0).gameObject;

        // Cache Animator on characterModel
        if (_characterModel != null)
        {
            _animController = _characterModel.GetComponent<Animator>();
            if (_animController != null)
            {
                _upperBodyLayerIndex = _animController.GetLayerIndex("UpperBody");
                // CharacterController drives position — Run anim root-motion Y would lift the whole player
                _animController.applyRootMotion = false;
            }
        }

        // Cache FixedJoystick on the scene
        _fixedJoystick = FindFirstObjectByType<FixedJoystick>();

        _enemyLayerMask = LayerMask.GetMask("Enemy");
        _enemyHits = new Collider[EnemyHitBufferSize];
        _groundHits = new RaycastHit[16];
        _mainCamera = Camera.main;

        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f;
        _audioSource.loop = false;

        if (_soundConfig != null)
            SoundManager.Ensure(_soundConfig);

        // Restore progress then equip the first inventory gun
        if (_userConfig != null)
            _userConfig.Load();

        // Cache ragdoll / renderer like Zombie so death uses the same flow
        _ragdollBodies = GetComponentsInChildren<Rigidbody>();
        _renderers = GetComponentsInChildren<Renderer>();
        CacheRagdollColliders();
        CacheRootColliders();
        IgnoreRagdollSelfCollision();
        SetRagdollKinematic(true);

        if (_dissolveShader == null)
            _dissolveShader = Shader.Find("Custom/EnemyDissolve");

        // health comes from UserConfig after Load
        health = _userConfig != null ? _userConfig.health : 0;
        _maxHealth = Mathf.Max(1, health);

        EquipInventoryWeapon();

        if (health <= 0)
            ApplyDie();
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
            _userConfig?.Save();
    }

    void OnApplicationQuit()
    {
        _userConfig?.Save();
    }

    void Update()
    {
        if (_isDead || _characterModel == null)
            return;

        // Win: stand still, do not run / do not shoot
        if (_movementLocked)
        {
            HoldLockedPose();
            return;
        }

        float horizontal = 0f;
        float vertical = 0f;
        bool isRunning = false;
        if (_fixedJoystick != null)
        {
            horizontal = _fixedJoystick.Horizontal;
            vertical = _fixedJoystick.Vertical;
            isRunning = horizontal != 0f || vertical != 0f;
        }

        // Toggle the Run bool from joystick state
        if (_animController != null)
            _animController.SetBool("Run", isRunning);

        // Find the nearest Enemy inside rangeAttack
        Transform nearestEnemy = FindNearestEnemy();
        bool hasEnemy = nearestEnemy != null;

        // UpperBody: Weight = 1 if an Enemy is in range, otherwise 0
        if (_animController != null && _upperBodyLayerIndex >= 0)
            _animController.SetLayerWeight(_upperBodyLayerIndex, hasEnemy ? 1f : 0f);

        // Enable Shoot if an Enemy is in rangeAttack, otherwise disable
        if (_animController != null)
            _animController.SetBool("Shoot", hasEnemy);

        // Play SFX + VFX each time a shoot anim cycle starts
        TryPlayFireSoundOnShootStart();

        // Enemy in range: lock facing, do not rotate with movement
        if (hasEnemy)
        {
            Vector3 lookDirection = nearestEnemy.position - _characterModel.transform.position;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude > 0.0001f)
                _characterModel.transform.rotation = Quaternion.LookRotation(lookDirection);
        }
        // Only rotate with movement once no Enemy is in range
        else if (isRunning)
        {
            Vector3 lookDirection = new Vector3(horizontal, 0f, vertical);
            Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
            _characterModel.transform.rotation = Quaternion.Slerp(
                _characterModel.transform.rotation,
                targetRotation,
                RotateSpeed * Time.deltaTime);
        }

        if (_characterController != null)
        {
            Vector3 move = Vector3.zero;
            if (isRunning)
                move = new Vector3(horizontal, 0f, vertical) * MoveSpeed;

            // Without gravity, ground/zombie/ragdoll bone hits push Y — slowly floats into the air
            if (_characterController.isGrounded)
                _verticalVelocity = GroundStickVelocity;
            else
                _verticalVelocity += Physics.gravity.y * Time.deltaTime;

            move.y = _verticalVelocity;
            _characterController.Move(move * Time.deltaTime);
        }

        if (_grenadeAiming)
            RefreshGrenadeAimIndicators();
    }

    /// <summary>
    /// Win: lock movement + aim, keep gravity so the player does not drift.
    /// </summary>
    public void LockMovement()
    {
        if (_isDead)
            return;

        _movementLocked = true;
        CancelGrenadeAim();
        HoldLockedPose();
    }

    void HoldLockedPose()
    {
        if (_animController != null)
        {
            _animController.SetBool("Run", false);
            _animController.SetBool("Shoot", false);
            if (_upperBodyLayerIndex >= 0)
                _animController.SetLayerWeight(_upperBodyLayerIndex, 0f);
        }

        if (_characterController == null || !_characterController.enabled)
            return;

        if (_characterController.isGrounded)
            _verticalVelocity = GroundStickVelocity;
        else
            _verticalVelocity += Physics.gravity.y * Time.deltaTime;

        Vector3 move = Vector3.zero;
        move.y = _verticalVelocity;
        _characterController.Move(move * Time.deltaTime);
    }

    /// <summary>
    /// Starts Arena-of-Valor-style grenade aim (indicator at the Player).
    /// </summary>
    public void BeginGrenadeAim()
    {
        if (_isDead || _movementLocked || _userConfig == null || _userConfig.grenade == null)
            return;

        if (_mainCamera == null)
            _mainCamera = Camera.main;

        CacheRangeExplosionFromPrefab();
        EnsureAimIndicators();
        _grenadeAiming = true;

        // Button press: aim at the Player's feet, only offset after dragging
        _grenadeAimPoint = ProjectToGround(transform.position);
        SetAimIndicatorsVisible(true);
        RefreshGrenadeAimIndicators();
    }

    /// <summary>
    /// Aim from a drag offset on the skill button (Arena of Valor style):
    /// left/right/up/down on screen → left/right/forward/back relative to the Camera.
    /// </summary>
    public void UpdateGrenadeAimByDrag(Vector2 screenDeltaFromBtn, float maxDragPixels)
    {
        if (!_grenadeAiming || _isDead)
            return;

        if (_mainCamera == null)
            _mainCamera = Camera.main;
        if (_mainCamera == null)
            return;

        float maxPixels = Mathf.Max(1f, maxDragPixels);
        float dist01 = Mathf.Clamp01(screenDeltaFromBtn.magnitude / maxPixels);

        Vector3 camForward = _mainCamera.transform.forward;
        camForward.y = 0f;
        Vector3 camRight = _mainCamera.transform.right;
        camRight.y = 0f;

        if (camForward.sqrMagnitude < 0.0001f)
            camForward = Vector3.forward;
        else
            camForward.Normalize();

        if (camRight.sqrMagnitude < 0.0001f)
            camRight = Vector3.right;
        else
            camRight.Normalize();

        // screen X → camRight, screen Y → camForward (drag up = camera forward)
        Vector3 worldDir = camRight * screenDeltaFromBtn.x + camForward * screenDeltaFromBtn.y;
        if (worldDir.sqrMagnitude > 0.0001f)
            worldDir.Normalize();
        else
            worldDir = Vector3.zero;

        float throwRange = Mathf.Max(0f, rangeGrenade) * dist01;
        Vector3 aim = transform.position + worldDir * throwRange;
        _grenadeAimPoint = ProjectToGround(aim);
        RefreshGrenadeAimIndicators();
    }

    /// <summary>
    /// Cancels aim and hides the indicator.
    /// </summary>
    public void CancelGrenadeAim()
    {
        _grenadeAiming = false;
        SetAimIndicatorsVisible(false);
    }

    /// <summary>
    /// Finger up: hide the indicator and throw the grenade to the aim point.
    /// </summary>
    public void ConfirmGrenadeThrow()
    {
        if (!_grenadeAiming || _isDead)
        {
            CancelGrenadeAim();
            return;
        }

        Vector3 target = _grenadeAimPoint;
        CancelGrenadeAim();
        ThrowGrenade(target);
    }

    void ThrowGrenade(Vector3 targetWorldPos)
    {
        if (_userConfig == null || _userConfig.grenade == null)
            return;

        Vector3 spawnPos = transform.position + Vector3.up * GrenadeSpawnHeight;
        Vector3 landing = ProjectToGround(targetWorldPos);
        // Keep the landing point from matching spawn (avoids NaN velocity / standing still)
        Vector3 flat = landing - spawnPos;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.01f)
            landing = spawnPos + GetDefaultThrowForward() * 0.5f;

        GameObject instance = Instantiate(_userConfig.grenade, spawnPos, Quaternion.identity);
        if (instance == null)
            return;

        GrenadeController controller = instance.GetComponent<GrenadeController>();
        if (controller == null)
            controller = instance.AddComponent<GrenadeController>();

        IgnoreGrenadePlayerCollision(instance);
        controller.Init(landing);
        SoundManager.Instance?.PlaySfx(SoundConfig.SfxGrenadeThrow);
    }

    Vector3 GetDefaultThrowForward()
    {
        if (_characterModel != null)
        {
            Vector3 f = _characterModel.transform.forward;
            f.y = 0f;
            if (f.sqrMagnitude > 0.0001f)
                return f.normalized;
        }

        if (_mainCamera == null)
            _mainCamera = Camera.main;
        if (_mainCamera != null)
        {
            Vector3 f = _mainCamera.transform.forward;
            f.y = 0f;
            if (f.sqrMagnitude > 0.0001f)
                return f.normalized;
        }

        return Vector3.forward;
    }

    void IgnoreGrenadePlayerCollision(GameObject grenade)
    {
        if (grenade == null || _characterController == null)
            return;

        Collider grenadeCol = grenade.GetComponent<Collider>();
        if (grenadeCol == null)
            return;

        Physics.IgnoreCollision(grenadeCol, _characterController, true);

        if (_rootColliders == null)
            return;

        for (int i = 0; i < _rootColliders.Length; i++)
        {
            Collider c = _rootColliders[i];
            if (c != null)
                Physics.IgnoreCollision(grenadeCol, c, true);
        }
    }

    void CacheRangeExplosionFromPrefab()
    {
        _cachedRangeExplosion = 2.5f;
        if (_userConfig == null || _userConfig.grenade == null)
            return;

        GrenadeController prefabCtrl = _userConfig.grenade.GetComponent<GrenadeController>();
        if (prefabCtrl != null)
            _cachedRangeExplosion = Mathf.Max(0.1f, prefabCtrl.RangeExplosion);
    }

    Vector3 ProjectToGround(Vector3 worldPoint)
    {
        float groundY = transform.position.y;
        Vector3 origin = worldPoint + Vector3.up * 5f;
        if (_groundHits == null)
            _groundHits = new RaycastHit[16];

        int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHits, 20f, ~0, QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _groundHits[i];
            if (hit.collider == null || !IsGroundPlaneTransform(hit.collider.transform))
                continue;

            if (hit.distance < bestDist)
            {
                bestDist = hit.distance;
                groundY = hit.point.y;
                found = true;
            }
        }

        if (!found)
            groundY = transform.position.y;

        return new Vector3(worldPoint.x, groundY, worldPoint.z);
    }

    static bool IsGroundPlaneTransform(Transform hit)
    {
        Transform t = hit;
        while (t != null)
        {
            if (t.name == PlaneName)
                return true;
            t = t.parent;
        }

        return false;
    }

    void EnsureAimIndicators()
    {
        // Olive / rust — matches the military HUD
        if (_maxRangeRing == null)
            _maxRangeRing = CreateAimRing("GrenadeMaxRangeRing", new Color(0.45f, 0.50f, 0.28f, 0.92f), 0.12f);
        if (_aoeRing == null)
            _aoeRing = CreateAimRing("GrenadeAoeRing", new Color(0.82f, 0.38f, 0.16f, 0.95f), 0.10f);
    }

    LineRenderer CreateAimRing(string objectName, Color color, float width)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(null, true);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.loop = true;
        lr.useWorldSpace = true;
        lr.positionCount = AimRingSegments;
        lr.widthMultiplier = width;
        lr.alignment = LineAlignment.View;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = color;
        lr.endColor = color;
        go.SetActive(false);
        return lr;
    }

    void SetAimIndicatorsVisible(bool visible)
    {
        if (_maxRangeRing != null)
            _maxRangeRing.gameObject.SetActive(visible);
        if (_aoeRing != null)
            _aoeRing.gameObject.SetActive(visible);
    }

    void RefreshGrenadeAimIndicators()
    {
        if (_maxRangeRing == null || _aoeRing == null)
            return;

        Vector3 center = transform.position;
        center.y += 0.05f;
        WriteRingPoints(_maxRangeRing, center, Mathf.Max(0f, rangeGrenade));

        Vector3 aoeCenter = _grenadeAimPoint;
        aoeCenter.y += 0.05f;
        WriteRingPoints(_aoeRing, aoeCenter, _cachedRangeExplosion);
    }

    static void WriteRingPoints(LineRenderer lr, Vector3 center, float radius)
    {
        if (lr == null)
            return;

        int count = lr.positionCount;
        if (count < 3)
            return;

        for (int i = 0; i < count; i++)
        {
            float t = (i / (float)count) * Mathf.PI * 2f;
            lr.SetPosition(i, center + new Vector3(Mathf.Cos(t) * radius, 0f, Mathf.Sin(t) * radius));
        }
    }

    /// <summary>
    /// Destroys the old gun (if any) then spawns the Inventory WeaponUse gun.
    /// </summary>
    public void EquipInventoryWeapon()
    {
        fireLocation = null;
        _currentWeapon = null;

        if (gunLocation != null)
        {
            for (int i = gunLocation.childCount - 1; i >= 0; i--)
            {
                Transform child = gunLocation.GetChild(i);
                if (child != null)
                    Destroy(child.gameObject);
            }
        }

        if (_userConfig == null || gunLocation == null)
            return;

        if (_userConfig.Inventory == null || _userConfig.Inventory.Count == 0)
            return;

        _userConfig.ClampWeaponUse();
        GameObject gunPrefab = _userConfig.Inventory[_userConfig.WeaponUse];
        if (gunPrefab == null)
            return;

        GameObject gunInstance = Instantiate(gunPrefab, gunLocation);
        gunInstance.transform.localPosition = Vector3.zero;
        gunInstance.transform.localRotation = Quaternion.identity;

        Transform found = FindChildByName(gunInstance.transform, FireLocationName);
        if (found != null)
            fireLocation = found;

        // Sound / Fire VFX from WeaponConfig of the equipped gun
        if (_weaponConfig != null)
            _currentWeapon = _weaponConfig.FindByGunPrefab(gunPrefab);

        ApplyCurrentWeaponFireSpeed();
    }

    /// <summary>
    /// Sets infantry_combat_shoot anim speed from the equipped weapon's fireSpeed.
    /// </summary>
    void ApplyCurrentWeaponFireSpeed()
    {
        if (_animController == null)
            return;

        float speed = _currentWeapon != null ? _currentWeapon.FireSpeed : 1f;
        _animController.SetFloat(FireSpeedAnimParam, speed);
    }

    /// <summary>
    /// Finds a child transform by name (recursive).
    /// </summary>
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

    /// <summary>
    /// When UpperBody starts (or loops) the shoot state, play sound + spawn Fire VFX from WeaponConfig.
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
            SpawnFireVfx();
            SpawnBullet();
            _lastShootCycleIndex = cycleIndex;
        }

        _shootAnimActive = true;
    }

    void SpawnFireVfx()
    {
        if (_currentWeapon == null || _currentWeapon.FireVfx == null || fireLocation == null)
            return;

        // Match fireLocation world pos/rot/scale, then unparent from the hierarchy
        GameObject vfx = Instantiate(_currentWeapon.FireVfx, fireLocation);
        Transform t = vfx.transform;
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
        t.SetParent(null, true);
    }

    /// <summary>
    /// Spawns Prefab Bullet at the gun prefab's FireLocation pos/rot (not parented).
    /// </summary>
    void SpawnBullet()
    {
        if (_currentWeapon == null || _currentWeapon.BulletPrefab == null || fireLocation == null)
            return;

        GameObject bullet = Instantiate(
            _currentWeapon.BulletPrefab,
            fireLocation.position,
            fireLocation.rotation);

        BulletController bulletController = bullet.GetComponent<BulletController>();
        if (bulletController == null)
            bulletController = bullet.AddComponent<BulletController>();

        bulletController.Init(
            _currentWeapon.Damage,
            _currentWeapon.BulletSpeed,
            fireLocation.forward);
    }

    void PlayFireSound()
    {
        if (_currentWeapon == null || _soundConfig == null || _audioSource == null)
            return;

        if (string.IsNullOrEmpty(_currentWeapon.Sound))
            return;

        SoundEntry entry = _soundConfig.FindSfxByName(_currentWeapon.Sound);
        if (entry == null || entry.Clip == null)
            return;

        _audioSource.PlayOneShot(entry.Clip, entry.Volume);
    }

#if UNITY_EDITOR
    void Reset()
    {
        // Assign a default config when the component is added
        _soundConfig = AssetDatabase.LoadAssetAtPath<SoundConfig>(SoundConfigAssetPath);
        _userConfig = AssetDatabase.LoadAssetAtPath<UserConfig>(UserConfigAssetPath);
        _weaponConfig = AssetDatabase.LoadAssetAtPath<WeaponConfig>(WeaponConfigAssetPath);
    }

    void OnValidate()
    {
        if (_soundConfig == null)
            _soundConfig = AssetDatabase.LoadAssetAtPath<SoundConfig>(SoundConfigAssetPath);
        if (_userConfig == null)
            _userConfig = AssetDatabase.LoadAssetAtPath<UserConfig>(UserConfigAssetPath);
        if (_weaponConfig == null)
            _weaponConfig = AssetDatabase.LoadAssetAtPath<WeaponConfig>(WeaponConfigAssetPath);
    }
#endif

    /// <summary>
    /// Takes enemy damage — subtract health; spawn hitVFX opposite the attack direction.
    /// </summary>
    public void TakeDamage(float damage, Transform attacker = null)
    {
        if (_isDead)
            return;

        int amount = Mathf.Max(0, Mathf.RoundToInt(damage));
        if (amount <= 0)
            return;

        SpawnHitVfx(attacker);

        health = Mathf.Max(0, health - amount);
        HealthChanged?.Invoke();

        if (health <= 0)
        {
            SoundManager.Instance?.PlaySfx(SoundConfig.SfxPlayerDie);
            ApplyDie();
        }
        else
        {
            SoundManager.Instance?.PlaySfx(SoundConfig.SfxPlayerHit);
        }
    }

    /// <summary>
    /// Spawns hitVFX at the Player position; rotation faces opposite the attacker's (Zombie) forward.
    /// </summary>
    void SpawnHitVfx(Transform attacker)
    {
        if (hitVFX == null)
            return;

        Quaternion rotation = Quaternion.identity;
        if (attacker != null)
        {
            Vector3 attackForward = attacker.forward;
            attackForward.y = 0f;
            if (attackForward.sqrMagnitude > 0.0001f)
                rotation = Quaternion.LookRotation(-attackForward.normalized);
        }

        Instantiate(hitVFX, transform.position, rotation);
    }

    /// <summary>
    /// Dies like a Zombie: disable control → ragdoll → dissolve → Destroy.
    /// </summary>
    void ApplyDie()
    {
        if (_isDead)
            return;

        _isDead = true;
        health = 0;

        // Disable CharacterController first — root capsule hitting bones would launch the body
        if (_characterController != null)
            _characterController.enabled = false;

        if (_animController != null)
        {
            _animController.SetBool("Run", false);
            _animController.SetBool("Shoot", false);
            if (_upperBodyLayerIndex >= 0)
                _animController.SetLayerWeight(_upperBodyLayerIndex, 0f);
            _animController.applyRootMotion = false;
            _animController.enabled = false;
        }

        if (_rootColliders != null)
        {
            for (int i = 0; i < _rootColliders.Length; i++)
            {
                if (_rootColliders[i] != null)
                    _rootColliders[i].enabled = false;
            }
        }

        ApplyDieRagdollAsync();
    }

    async Awaitable ApplyDieRagdollAsync()
    {
        // Wait 1 frame so CharacterController/root collider is fully off
        await Awaitable.NextFrameAsync();
        if (this == null)
            return;

        Physics.SyncTransforms();
        IgnoreRagdollSelfCollision();
        SetRagdollKinematic(false);
        await DissolveAfterDieAsync();
    }

    async Awaitable DissolveAfterDieAsync()
    {
        await Awaitable.WaitForSecondsAsync(_ragdollWaitSeconds);

        if (!PrepareDissolveMaterials())
        {
            Destroy(gameObject);
            return;
        }

        float elapsed = 0f;
        while (elapsed < _dissolveDuration)
        {
            elapsed += Time.deltaTime;
            SetDissolveAmount(Mathf.Clamp01(elapsed / _dissolveDuration));
            await Awaitable.NextFrameAsync();
        }

        SetDissolveAmount(1f);
        Destroy(gameObject);
    }

    bool PrepareDissolveMaterials()
    {
        if (_dissolveShader == null)
            _dissolveShader = Shader.Find("Custom/EnemyDissolve");

        if (_dissolveShader == null || _renderers == null || _renderers.Length == 0)
            return false;

        int materialCount = 0;
        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer renderer = _renderers[i];
            if (renderer == null)
                continue;

            Material[] shared = renderer.sharedMaterials;
            if (shared != null)
                materialCount += shared.Length;
        }

        if (materialCount == 0)
            return false;

        _dissolveMaterials = new Material[materialCount];
        int writeIndex = 0;

        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer renderer = _renderers[i];
            if (renderer == null)
                continue;

            Material[] shared = renderer.sharedMaterials;
            if (shared == null || shared.Length == 0)
                continue;

            Material[] instances = new Material[shared.Length];
            for (int m = 0; m < shared.Length; m++)
            {
                Material source = shared[m];
                Material dissolveMat = new Material(_dissolveShader);
                CopySourceAppearance(source, dissolveMat);
                dissolveMat.SetFloat(DissolveAmountId, 0f);
                instances[m] = dissolveMat;
                _dissolveMaterials[writeIndex++] = dissolveMat;
            }

            renderer.materials = instances;
        }

        return writeIndex > 0;
    }

    void CopySourceAppearance(Material source, Material dissolveMat)
    {
        if (source == null || dissolveMat == null)
            return;

        if (source.HasProperty(BaseMapId))
            dissolveMat.SetTexture(BaseMapId, source.GetTexture(BaseMapId));
        else if (source.HasProperty(MainTexId))
            dissolveMat.SetTexture(BaseMapId, source.GetTexture(MainTexId));

        if (source.HasProperty(BaseColorId))
            dissolveMat.SetColor(BaseColorId, source.GetColor(BaseColorId));
        else if (source.HasProperty(ColorId))
            dissolveMat.SetColor(BaseColorId, source.GetColor(ColorId));
    }

    void SetDissolveAmount(float amount)
    {
        if (_dissolveMaterials == null)
            return;

        for (int i = 0; i < _dissolveMaterials.Length; i++)
        {
            Material mat = _dissolveMaterials[i];
            if (mat == null)
                continue;

            mat.SetFloat(DissolveAmountId, amount);
        }
    }

    void SetRagdollKinematic(bool kinematic)
    {
        if (_ragdollBodies == null)
            return;

        for (int i = 0; i < _ragdollBodies.Length; i++)
        {
            Rigidbody rb = _ragdollBodies[i];
            if (rb == null)
                continue;

            // Clear leftover velocity + limit depenetration push (avoids launch)
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.maxDepenetrationVelocity = 1f;
            rb.isKinematic = kinematic;
            if (!kinematic)
                rb.WakeUp();
        }

        // Alive: disable bone colliders — CC.Move hitting bones launches. Dead: re-enable for ragdoll
        if (_ragdollColliders == null)
            return;

        for (int i = 0; i < _ragdollColliders.Length; i++)
        {
            if (_ragdollColliders[i] != null)
                _ragdollColliders[i].enabled = !kinematic;
        }
    }

    void CacheRagdollColliders()
    {
        if (_ragdollBodies == null || _ragdollBodies.Length == 0)
        {
            _ragdollColliders = new Collider[0];
            return;
        }

        int count = 0;
        for (int i = 0; i < _ragdollBodies.Length; i++)
        {
            if (_ragdollBodies[i] != null && _ragdollBodies[i].GetComponent<Collider>() != null)
                count++;
        }

        _ragdollColliders = new Collider[count];
        int index = 0;
        for (int i = 0; i < _ragdollBodies.Length; i++)
        {
            Rigidbody rb = _ragdollBodies[i];
            if (rb == null)
                continue;

            Collider col = rb.GetComponent<Collider>();
            if (col == null)
                continue;

            _ragdollColliders[index++] = col;
        }
    }

    void CacheRootColliders()
    {
        Collider[] all = GetComponents<Collider>();
        int count = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].GetComponent<Rigidbody>() == null)
                count++;
        }

        _rootColliders = new Collider[count];
        int index = 0;
        for (int i = 0; i < all.Length; i++)
        {
            Collider col = all[i];
            if (col == null || col.GetComponent<Rigidbody>() != null)
                continue;

            _rootColliders[index++] = col;
        }
    }

    void IgnoreRagdollSelfCollision()
    {
        if (_ragdollColliders == null)
            return;

        for (int i = 0; i < _ragdollColliders.Length; i++)
        {
            Collider a = _ragdollColliders[i];
            if (a == null)
                continue;

            for (int j = i + 1; j < _ragdollColliders.Length; j++)
            {
                Collider b = _ragdollColliders[j];
                if (b == null)
                    continue;

                Physics.IgnoreCollision(a, b, true);
            }
        }
    }

    void OnDestroy()
    {
        if (_maxRangeRing != null)
            Destroy(_maxRangeRing.gameObject);
        if (_aoeRing != null)
            Destroy(_aoeRing.gameObject);

        if (_dissolveMaterials == null)
            return;

        for (int i = 0; i < _dissolveMaterials.Length; i++)
        {
            if (_dissolveMaterials[i] != null)
                Destroy(_dissolveMaterials[i]);
        }
    }

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
        // Drag the sphere gizmo to edit rangeAttack in the Scene view
        Handles.color = new Color(1f, 0.3f, 0.2f, 0.9f);
        float newRange = Handles.RadiusHandle(Quaternion.identity, transform.position, Mathf.Max(0f, _rangeAttack));
        if (!Mathf.Approximately(newRange, _rangeAttack))
        {
            Undo.RecordObject(this, "Edit Range Attack");
            _rangeAttack = Mathf.Max(0f, newRange);
            EditorUtility.SetDirty(this);
        }

        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.15f);
        Gizmos.DrawSphere(transform.position, _rangeAttack);
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, _rangeAttack);

        // rangeGrenade gizmo (olive — matches HUD)
        Handles.color = new Color(0.45f, 0.50f, 0.28f, 0.9f);
        float newGrenadeRange = Handles.RadiusHandle(Quaternion.identity, transform.position, Mathf.Max(0f, rangeGrenade));
        if (!Mathf.Approximately(newGrenadeRange, rangeGrenade))
        {
            Undo.RecordObject(this, "Edit Range Grenade");
            rangeGrenade = Mathf.Max(0f, newGrenadeRange);
            EditorUtility.SetDirty(this);
        }

        Gizmos.color = new Color(0.45f, 0.50f, 0.28f, 0.12f);
        Gizmos.DrawSphere(transform.position, rangeGrenade);
        Gizmos.color = new Color(0.45f, 0.50f, 0.28f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, rangeGrenade);
    }
#endif
}
