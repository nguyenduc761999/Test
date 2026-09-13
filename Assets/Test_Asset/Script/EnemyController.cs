using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyController : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] EnemyConfig _enemyConfig;

    public bool die;

    [HideInInspector] public bool walk;
    [HideInInspector] public bool run;
    [HideInInspector] public Animator animController;

    public float walkSpeed = 2f;
    public float runSpeed = 3.5f;
    public float rangeAttackPlayer = 1.5f;
    public float health;

    /// <summary>VFX on bullet hit — spawn at the Enemy position, rotated opposite the attack direction.</summary>
    [SerializeField] GameObject hitVFX;

    [SerializeField] float _ragdollWaitSeconds = 2f;
    [SerializeField] float _dissolveDuration = 1.5f;
    [SerializeField] Shader _dissolveShader;
    [SerializeField] float _destinationRefreshSeconds = 0.25f;
    [SerializeField] float _navMeshSampleRadius = 5f;
    [SerializeField] float _walkSpeedThreshold = 0.1f;
    [SerializeField] float _agentAvoidanceRadius = 0.45f;
    [SerializeField] float _attackLookDegreesPerSecond = 540f;

#if UNITY_EDITOR
    const string EnemyConfigAssetPath = "Assets/Test_Asset/Config/EnemyConfig.asset";
#endif

    static readonly int DissolveAmountId = Shader.PropertyToID("_DissolveAmount");
    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int WalkHash = Animator.StringToHash("Walk");
    static readonly int RunHash = Animator.StringToHash("Run");
    const string ZombieAttackStateName = "ZombieAttack";
    const string ZombieIdleStateName = "Zombie Idle";
    // Golden-angle slots around the player so chase headings do not overlap
    const float ChaseSlotGoldenAngleDegrees = 137.508f;

    static int _nextChaseSlot;
    static readonly List<EnemyController> _activeEnemies = new List<EnemyController>(32);

    const float HitCapsuleRadius = 0.65f;
    const float HitCapsuleBottom = 0.1f;
    const float HitCapsuleTop = 1.85f;
    const float HitCapsuleRadiusPadding = 0.25f;
    const float MaxRagdollSpeed = 6.5f;
    const float MaxExplosionRagdollSpeed = 16f;
    const float MaxExplosionRagdollAngularSpeed = 20f;
    const float MaxDepenetrationSpeed = 1.5f;

    NavMeshAgent _agent;
    Transform _player;
    PlayerController _playerController;
    Rigidbody[] _ragdollBodies;
    Collider[] _ragdollColliders;
    Collider[] _rootColliders;
    Renderer[] _renderers;
    Material[] _dissolveMaterials;
    bool _isDead;
    bool _frozen;
    float _destinationTimer;
    bool _hasWalkParam;
    bool _hasRunParam;
    int _chaseSlot;
    Rigidbody _hipsBody;
    CharacterJoint[] _ragdollJoints;
    Transform[] _skinBones;
    Vector3[] _skinBoneLocalPositions;
    Quaternion[] _skinBoneLocalRotations;
    Mesh[] _bakedDissolveMeshes;
    CharacterController _playerCharacterController;
    bool _isExplosionDeath;
    bool _ragdollActive;
    bool _hasPendingExplosion;
    Vector3 _pendingExplosionPos;
    float _pendingExplosionForce;
    float _pendingExplosionRadius;
    float _pendingExplosionUpwards;
    float _hitCapsuleRadius = HitCapsuleRadius;
    float _hitCapsuleBottom = HitCapsuleBottom;
    float _hitCapsuleTop = HitCapsuleTop;

    void Awake()
    {
        // Cache Animator, NavMeshAgent, and ragdoll Rigidbodies on bones
        if (animController == null)
            animController = GetComponentInChildren<Animator>();
        _agent = GetComponent<NavMeshAgent>();
        _ragdollBodies = GetComponentsInChildren<Rigidbody>();
        _renderers = GetComponentsInChildren<Renderer>();
        CacheRagdollColliders();
        CacheRootColliders();
        IgnoreRagdollSelfCollision();
        SetRagdollKinematic(true);
        CacheHipsBody();
        CacheSkinBones();
        CacheRagdollJoints();
        // Alive: only the Enemy-layer root capsule takes hits — bone colliders stay off so the hitbox stays aligned
        SetRagdollCollidersEnabled(false);
        ApplySkinnedMeshBoneQuality();

        // A: offset radius / avoidance / priority so agents differ
        ConfigureAgentAvoidance();
        // B: one slot around the player per enemy
        _chaseSlot = _nextChaseSlot++;

        CacheHitCapsule();

        // Agent drives position → disable root motion to avoid fighting it
        if (animController != null)
        {
            animController.applyRootMotion = false;
            _hasWalkParam = HasAnimatorParam(WalkHash, AnimatorControllerParameterType.Bool);
            _hasRunParam = HasAnimatorParam(RunHash, AnimatorControllerParameterType.Bool);
        }

        _playerController = FindFirstObjectByType<PlayerController>();
        if (_playerController != null)
        {
            _player = _playerController.transform;
            _playerCharacterController = _playerController.GetComponent<CharacterController>();
        }

        if (_dissolveShader == null)
            _dissolveShader = Shader.Find("Custom/EnemyDissolve");

        // Health comes from EnemyConfig for the matching instance
        ApplyHealthFromConfig();
    }

    void OnEnable()
    {
        _activeEnemies.Add(this);
    }

    void OnDisable()
    {
        _activeEnemies.Remove(this);
    }

    /// <summary>
    /// Nearest living enemy whose root is inside range (transform distance, not physics).
    /// </summary>
    public static EnemyController FindNearestAlive(Vector3 position, float range)
    {
        EnemyController nearest = null;
        float nearestDistSq = range * range;
        for (int i = 0; i < _activeEnemies.Count; i++)
        {
            EnemyController enemy = _activeEnemies[i];
            if (enemy == null || enemy.die || enemy._isDead)
                continue;

            float distSq = (enemy.transform.position - position).sqrMagnitude;
            if (distSq <= nearestDistSq)
            {
                nearestDistSq = distSq;
                nearest = enemy;
            }
        }

        return nearest;
    }

    public static int ActiveEnemyCount => _activeEnemies.Count;

    public static EnemyController GetActiveEnemy(int index)
    {
        if (index < 0 || index >= _activeEnemies.Count)
            return null;
        return _activeEnemies[index];
    }

    /// <summary>
    /// True if the shot segment hits this enemy's standing capsule (NavMeshAgent transform).
    /// </summary>
    public bool HitByShot(Vector3 from, Vector3 to, float bulletRadius)
    {
        if (_isDead || die)
            return false;

        Vector3 bottom = transform.position + Vector3.up * _hitCapsuleBottom;
        Vector3 top = transform.position + Vector3.up * _hitCapsuleTop;
        float limit = _hitCapsuleRadius + Mathf.Max(0f, bulletRadius);
        return DistanceSegmentSegment(from, to, bottom, top) <= limit;
    }

    void Start()
    {
        // Randomly pick walk or run → that one true, the other false
        bool chooseWalk = Random.value < 0.5f;
        walk = chooseWalk;
        run = !chooseWalk;
        ApplyMoveMode();

        // Place the agent on the NavMesh before chasing
        EnsureOnNavMesh();
        SoundManager.Instance?.PlaySfx(SoundConfig.SfxZombieSpawn);
    }

    /// <summary>
    /// Assigns runtime health from the matching EnemyEntry.Health in EnemyConfig.
    /// </summary>
    void ApplyHealthFromConfig()
    {
        if (_enemyConfig == null)
            return;

        EnemyEntry entry = _enemyConfig.FindByInstance(gameObject);
        if (entry == null)
            return;

        health = entry.Health;
    }

    void ApplyMoveMode()
    {
        // Apply agent speed for the randomly chosen mode
        if (_agent != null)
            _agent.speed = walk ? walkSpeed : runSpeed;

        if (animController == null)
            return;

        // Animator parameter matches the bool that is currently true
        if (_hasWalkParam)
            animController.SetBool(WalkHash, walk);
        if (_hasRunParam)
            animController.SetBool(RunHash, run);
    }

    void Update()
    {
        // Ticking die = true in the Inspector → ragdoll once
        if (die && !_isDead)
            ApplyDie();

        if (_isDead)
        {
            ClampRagdollSpeed();
            return;
        }

        // Lose / match locked → stand still, do not chase
        if (_frozen)
            return;

        // Player died → stop chasing and attacking
        if (_playerController == null || _playerController.IsDead)
        {
            FreezeInPlace();
            return;
        }

        ChasePlayer();
        UpdateMoveAnimation();
    }

    void FixedUpdate()
    {
        if (!_isDead)
            return;

        if (_ragdollActive && _hasPendingExplosion)
            ApplyPendingExplosion();

        ClampRagdollSpeed();
    }

    void EnsureOnNavMesh()
    {
        if (_agent == null)
            return;

        if (_agent.isOnNavMesh)
            return;

        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, _navMeshSampleRadius, NavMesh.AllAreas))
            return;

        _agent.Warp(hit.position);
    }

    void ChasePlayer()
    {
        if (_agent == null || _player == null)
            return;

        if (!_agent.isOnNavMesh)
        {
            EnsureOnNavMesh();
            if (!_agent.isOnNavMesh)
                return;
        }

        // In ZombieAttack → lock movement, look at player, wait for the anim to finish before re-evaluating
        if (IsPlayingZombieAttack())
        {
            StopAgentMovement();
            SetMoveAnimActive(false);
            FacePlayerSmooth();
            return;
        }

        // Inside rangeAttackPlayer → stop + lookat; only chase again outside range
        if (IsPlayerInAttackRange())
        {
            StopAgentMovement();
            SetMoveAnimActive(false);
            FacePlayerSmooth();
            TryPlayZombieAttack();
            return;
        }

        ResumeAgentMovement();
        SetAgentUpdateRotation(true);

        _destinationTimer -= Time.deltaTime;
        if (_destinationTimer > 0f)
            return;

        _destinationTimer = _destinationRefreshSeconds;
        // B: destination offset to a slot around the player, not stacked on one point
        _agent.SetDestination(GetChaseDestination());
    }

    void ConfigureAgentAvoidance()
    {
        if (_agent == null)
            return;

        // A: raise avoidance radius + high-quality avoidance + random priority
        _agent.radius = Mathf.Max(_agent.radius, _agentAvoidanceRadius);
        _agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
        _agent.avoidancePriority = Random.Range(0, 99);

        // Must equal rangeAttack: stop as soon as in attack range
        SyncStoppingDistanceToAttackRange();
    }

    void SyncStoppingDistanceToAttackRange()
    {
        if (_agent == null)
            return;

        _agent.stoppingDistance = Mathf.Max(0f, rangeAttackPlayer);
    }

    Vector3 GetChaseDestination()
    {
        if (_player == null)
            return transform.position;

        // Destination = player (no far offset): stoppingDistance == rangeAttack → stop at attack range
        // Slot is only a slight offset around the player to avoid stacking on one point
        float ringRadius = 0.05f;
        float angleRad = _chaseSlot * ChaseSlotGoldenAngleDegrees * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad)) * ringRadius;
        Vector3 desired = _player.position + offset;

        if (NavMesh.SamplePosition(desired, out NavMeshHit hit, _navMeshSampleRadius, NavMesh.AllAreas))
            return hit.position;

        if (NavMesh.SamplePosition(_player.position, out hit, _navMeshSampleRadius, NavMesh.AllAreas))
            return hit.position;

        return _player.position;
    }

    bool IsPlayerInAttackRange()
    {
        if (_player == null)
            return false;

        float range = Mathf.Max(0f, rangeAttackPlayer);
        float rangeSq = range * range;
        return (_player.position - transform.position).sqrMagnitude <= rangeSq;
    }

    bool IsPlayingZombieAttack()
    {
        if (animController == null)
            return false;

        AnimatorStateInfo current = animController.GetCurrentAnimatorStateInfo(0);
        if (current.IsName(ZombieAttackStateName))
        {
            // Attack cycle finished (no longer in transition) → treat as done
            if (current.normalizedTime >= 1f && !animController.IsInTransition(0))
                return false;
            return true;
        }

        // Blending into ZombieAttack
        if (animController.IsInTransition(0))
        {
            AnimatorStateInfo next = animController.GetNextAnimatorStateInfo(0);
            if (next.IsName(ZombieAttackStateName))
                return true;
        }

        return false;
    }

    void TryPlayZombieAttack()
    {
        if (animController == null)
            return;

        if (IsPlayingZombieAttack())
            return;

        animController.CrossFadeInFixedTime(ZombieAttackStateName, 0.1f, 0, 0f);
        SoundManager.Instance?.PlaySfxAt(SoundConfig.SfxZombieAttack, transform.position);
    }

    void StopAgentMovement()
    {
        if (_agent == null || !_agent.isOnNavMesh)
            return;

        // Zero velocity immediately — a fast run still slides a few frames if only isStopped/ResetPath is used
        _agent.velocity = Vector3.zero;

        if (!_agent.isStopped)
            _agent.isStopped = true;

        if (_agent.hasPath)
            _agent.ResetPath();

        // Attack handles rotation → disable agent rotation
        SetAgentUpdateRotation(false);
    }

    void ResumeAgentMovement()
    {
        if (_agent == null || !_agent.isOnNavMesh)
            return;

        if (_agent.isStopped)
            _agent.isStopped = false;
    }

    void SetAgentUpdateRotation(bool enabled)
    {
        if (_agent == null)
            return;

        _agent.updateRotation = enabled;
    }

    /// <summary>
    /// Turns to face the player quickly but smoothly (RotateTowards in degrees/sec).
    /// </summary>
    void FacePlayerSmooth()
    {
        if (_player == null)
            return;

        Vector3 lookDirection = _player.position - transform.position;
        lookDirection.y = 0f;
        if (lookDirection.sqrMagnitude < 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
        float maxDegrees = Mathf.Max(0f, _attackLookDegreesPerSecond) * Time.deltaTime;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, maxDegrees);
    }

    void SetMoveAnimActive(bool active)
    {
        if (animController == null)
            return;

        if (_hasWalkParam)
            animController.SetBool(WalkHash, active && walk);
        if (_hasRunParam)
            animController.SetBool(RunHash, active && run);
    }

    void UpdateMoveAnimation()
    {
        if (animController == null || _agent == null || !_agent.isOnNavMesh)
            return;

        // Attack / in range → do not update walk/run from velocity
        if (IsPlayingZombieAttack() || IsPlayerInAttackRange())
            return;

        bool isMoving = !_agent.isStopped
            && _agent.velocity.sqrMagnitude > _walkSpeedThreshold * _walkSpeedThreshold
            && _agent.remainingDistance > _agent.stoppingDistance;

        SetMoveAnimActive(isMoving);
    }

    /// <summary>
    /// Animation Event: apply damage only if the player is still inside rangeAttackPlayer.
    /// Damage comes from the EnemyConfig entry for this prefab.
    /// </summary>
    public void attackPlayer()
    {
        if (_isDead || _frozen || _player == null || _playerController == null || _playerController.IsDead)
            return;

        float range = Mathf.Max(0f, rangeAttackPlayer);
        float rangeSq = range * range;
        if ((_player.position - transform.position).sqrMagnitude > rangeSq)
            return;

        if (_enemyConfig == null)
            return;

        // Damage from EnemyConfig by prefab → subtract PlayerController health
        _enemyConfig.AttackPlayer(_playerController, gameObject);
    }

    /// <summary>
    /// Takes bullet damage — subtract health; spawn hitVFX opposite the attack direction.
    /// </summary>
    public void TakeDamage(float damage, Transform attacker = null)
    {
        if (_isDead)
            return;

        float amount = Mathf.Max(0f, damage);
        if (amount <= 0f)
            return;

        SpawnHitVfx(attacker);

        health = Mathf.Max(0f, health - amount);
        if (health <= 0f)
            ApplyDie();
        else
            SoundManager.Instance?.PlaySfx(SoundConfig.SfxFleshHit);
    }

    /// <summary>
    /// Lose: stand still, disable walk/run, do not chase / do not attack.
    /// </summary>
    public void FreezeInPlace()
    {
        if (_isDead || _frozen)
            return;

        _frozen = true;
        StopAgentMovement();
        SetMoveAnimActive(false);

        if (animController != null)
            animController.CrossFadeInFixedTime(ZombieIdleStateName, 0.1f, 0, 0f);
    }

    /// <summary>
    /// Die immediately from an explosion with moderate ragdoll force (do not zero velocity after force).
    /// </summary>
    public void DieFromExplosion(Vector3 explosionPos, float force, float radius, float upwardsModifier)
    {
        if (_isDead)
            return;

        health = 0f;
        _isExplosionDeath = true;
        _hasPendingExplosion = true;
        _pendingExplosionPos = explosionPos;
        _pendingExplosionForce = force;
        _pendingExplosionRadius = radius;
        _pendingExplosionUpwards = upwardsModifier;
        ApplyDie();
    }

    /// <summary>
    /// Spawns hitVFX at the Enemy position; rotation faces opposite the attacker's (bullet) forward.
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

        Instantiate(hitVFX, transform.position + Vector3.up * 0.9f, rotation);
    }

#if UNITY_EDITOR
    void Reset()
    {
        if (_enemyConfig == null)
            _enemyConfig = AssetDatabase.LoadAssetAtPath<EnemyConfig>(EnemyConfigAssetPath);
    }

    void OnValidate()
    {
        if (_enemyConfig == null)
            _enemyConfig = AssetDatabase.LoadAssetAtPath<EnemyConfig>(EnemyConfigAssetPath);

        rangeAttackPlayer = Mathf.Max(0f, rangeAttackPlayer);
        if (_agent == null)
            _agent = GetComponent<NavMeshAgent>();
        SyncStoppingDistanceToAttackRange();
    }

    void OnDrawGizmosSelected()
    {
        // Drag gizmo to edit rangeAttackPlayer in the Scene view
        Handles.color = new Color(1f, 0.55f, 0.1f, 0.9f);
        float newRange = Handles.RadiusHandle(Quaternion.identity, transform.position, Mathf.Max(0f, rangeAttackPlayer));
        if (!Mathf.Approximately(newRange, rangeAttackPlayer))
        {
            Undo.RecordObject(this, "Edit Range Attack Player");
            rangeAttackPlayer = Mathf.Max(0f, newRange);
            if (_agent == null)
                _agent = GetComponent<NavMeshAgent>();
            SyncStoppingDistanceToAttackRange();
            EditorUtility.SetDirty(this);
            if (_agent != null)
                EditorUtility.SetDirty(_agent);
        }

        Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.15f);
        Gizmos.DrawSphere(transform.position, rangeAttackPlayer);
        Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, rangeAttackPlayer);
    }
#endif

    bool HasAnimatorParam(int hash, AnimatorControllerParameterType type)
    {
        if (animController == null)
            return false;

        AnimatorControllerParameter[] parameters = animController.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter p = parameters[i];
            if (p.type == type && p.nameHash == hash)
                return true;
        }

        return false;
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

            // Clear leftover velocity before changing state (avoids launch)
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = kinematic;
        }
    }

    public void ApplyDie()
    {
        if (_isDead)
            return;

        _isDead = true;
        die = true;
        SoundManager.Instance?.PlaySfxAt(SoundConfig.SfxZombieDie, transform.position);

        // Disable pathfinding before ragdoll without letting the agent warp the pose
        if (_agent != null)
        {
            Vector3 freezePos = transform.position;
            Quaternion freezeRot = transform.rotation;
            if (_agent.isOnNavMesh)
                _agent.isStopped = true;
            _agent.updatePosition = false;
            _agent.updateRotation = false;
            _agent.enabled = false;
            transform.SetPositionAndRotation(freezePos, freezeRot);
        }

        if (_hasWalkParam && animController != null)
            animController.SetBool(WalkHash, false);
        if (_hasRunParam && animController != null)
            animController.SetBool(RunHash, false);

        // Keep Animator enabled this frame so GPU skinning still has a valid pose
        PrepareRenderersForRagdoll();
        if (animController != null)
        {
            animController.writeDefaultValuesOnDisable = false;
            animController.keepAnimatorStateOnDisable = true;
            animController.speed = 0f;
        }

        // Disable the root collider (avoids hitting bone capsules → launch)
        if (_rootColliders != null)
        {
            for (int i = 0; i < _rootColliders.Length; i++)
            {
                if (_rootColliders[i] != null)
                    _rootColliders[i].enabled = false;
            }
        }

        Physics.SyncTransforms();
        DissolveAfterDieAsync();
    }

    async Awaitable DissolveAfterDieAsync()
    {
        // Wait 1 frame so the root collider is fully off before ragdoll (avoids launching bones)
        await Awaitable.NextFrameAsync();
        if (this == null)
            return;

        FreezeSkinnedPoseForRagdoll();
        RelaxRagdollJoints();
        IgnoreRagdollExternalCollisions();

        // One extra frame so GPU skinning uploads the restored bone matrices before PhysX starts
        await Awaitable.NextFrameAsync();
        if (this == null)
            return;

        Physics.SyncTransforms();
        SetRagdollCollidersEnabled(true);
        SetRagdollKinematic(false);
        SetRagdollPhysicsSettings();
        _ragdollActive = true;

        await Awaitable.WaitForSecondsAsync(_ragdollWaitSeconds);
        if (this == null)
            return;

        // Bake a static posed mesh before dissolve — Custom/EnemyDissolve has no GPU skinning
        if (!PrepareDissolveMaterials())
        {
            await Awaitable.WaitForSecondsAsync(_dissolveDuration);
            if (this != null)
                Destroy(gameObject);
            return;
        }

        float elapsed = 0f;
        while (elapsed < _dissolveDuration)
        {
            elapsed += Time.deltaTime;
            SetDissolveAmount(Mathf.Clamp01(elapsed / _dissolveDuration));
            await Awaitable.NextFrameAsync();
            if (this == null)
                return;
        }

        SetDissolveAmount(1f);
        Destroy(gameObject);
    }

    // Force 4 bone weights — Mobile quality can drop to 2 and collapse the mesh on ragdoll
    void ApplySkinnedMeshBoneQuality()
    {
        if (_renderers == null)
            return;

        for (int i = 0; i < _renderers.Length; i++)
        {
            SkinnedMeshRenderer smr = _renderers[i] as SkinnedMeshRenderer;
            if (smr == null)
                continue;

            smr.quality = SkinQuality.Bone4;
        }
    }

    void PrepareRenderersForRagdoll()
    {
        if (animController != null)
            animController.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        if (_renderers == null)
            return;

        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer renderer = _renderers[i];
            if (renderer == null)
                continue;

            renderer.allowOcclusionWhenDynamic = false;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            SkinnedMeshRenderer smr = renderer as SkinnedMeshRenderer;
            if (smr == null)
                continue;

            smr.updateWhenOffscreen = true;
            smr.quality = SkinQuality.Bone4;
            smr.skinnedMotionVectors = false;
        }
    }

    void SetRagdollPhysicsSettings()
    {
        if (_ragdollBodies == null)
            return;

        for (int i = 0; i < _ragdollBodies.Length; i++)
        {
            Rigidbody rb = _ragdollBodies[i];
            if (rb == null)
                continue;

            // Discrete: ContinuousSpeculative + start overlap launches the corpse on mobile
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.maxDepenetrationVelocity = MaxDepenetrationSpeed;
            rb.interpolation = _isExplosionDeath || rb == _hipsBody
                ? RigidbodyInterpolation.Interpolate
                : RigidbodyInterpolation.None;
        }
    }

    void CacheSkinBones()
    {
        int count = 0;
        if (_renderers != null)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                SkinnedMeshRenderer smr = _renderers[i] as SkinnedMeshRenderer;
                if (smr == null)
                    continue;

                if (smr.rootBone != null)
                    count++;

                Transform[] bones = smr.bones;
                if (bones == null)
                    continue;

                for (int b = 0; b < bones.Length; b++)
                {
                    if (bones[b] != null)
                        count++;
                }
            }
        }

        _skinBones = new Transform[count];
        _skinBoneLocalPositions = new Vector3[count];
        _skinBoneLocalRotations = new Quaternion[count];
        int index = 0;
        if (_renderers == null)
            return;

        for (int i = 0; i < _renderers.Length; i++)
        {
            SkinnedMeshRenderer smr = _renderers[i] as SkinnedMeshRenderer;
            if (smr == null)
                continue;

            if (smr.rootBone != null)
                _skinBones[index++] = smr.rootBone;

            Transform[] bones = smr.bones;
            if (bones == null)
                continue;

            for (int b = 0; b < bones.Length; b++)
            {
                if (bones[b] != null)
                    _skinBones[index++] = bones[b];
            }
        }
    }

    void CacheRagdollJoints()
    {
        _ragdollJoints = GetComponentsInChildren<CharacterJoint>();
    }

    void CaptureSkinBonePoses()
    {
        if (_skinBones == null)
            return;

        for (int i = 0; i < _skinBones.Length; i++)
        {
            Transform bone = _skinBones[i];
            if (bone == null)
                continue;

            _skinBoneLocalPositions[i] = bone.localPosition;
            _skinBoneLocalRotations[i] = bone.localRotation;
        }
    }

    void RestoreSkinBonePoses()
    {
        if (_skinBones == null)
            return;

        for (int i = 0; i < _skinBones.Length; i++)
        {
            Transform bone = _skinBones[i];
            if (bone == null)
                continue;

            bone.localPosition = _skinBoneLocalPositions[i];
            bone.localRotation = _skinBoneLocalRotations[i];
        }
    }

    void FreezeSkinnedPoseForRagdoll()
    {
        if (animController != null)
        {
            animController.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animController.writeDefaultValuesOnDisable = false;
            animController.keepAnimatorStateOnDisable = true;
            animController.speed = 0f;
            if (animController.enabled)
                animController.Update(0f);
        }

        CaptureSkinBonePoses();

        if (animController != null)
            animController.enabled = false;

        RestoreSkinBonePoses();
        Physics.SyncTransforms();

        if (_renderers == null)
            return;

        for (int i = 0; i < _renderers.Length; i++)
        {
            SkinnedMeshRenderer smr = _renderers[i] as SkinnedMeshRenderer;
            if (smr == null)
                continue;

            smr.forceMatrixRecalculationPerRender = true;
            smr.updateWhenOffscreen = true;
            smr.quality = SkinQuality.Bone4;
        }
    }

    void RelaxRagdollJoints()
    {
        if (_ragdollJoints == null)
            return;

        for (int i = 0; i < _ragdollJoints.Length; i++)
        {
            CharacterJoint joint = _ragdollJoints[i];
            if (joint == null)
                continue;

            joint.enableProjection = false;
        }
    }

    void IgnoreRagdollExternalCollisions()
    {
        if (_ragdollColliders == null)
            return;

        if (_playerCharacterController != null)
        {
            for (int i = 0; i < _ragdollColliders.Length; i++)
            {
                Collider ragdollCol = _ragdollColliders[i];
                if (ragdollCol == null)
                    continue;

                Physics.IgnoreCollision(ragdollCol, _playerCharacterController, true);
            }
        }

        for (int e = 0; e < _activeEnemies.Count; e++)
        {
            EnemyController other = _activeEnemies[e];
            if (other == null || other == this)
                continue;

            if (other._isDead)
                continue;

            IgnoreRagdollAgainst(other._rootColliders);
        }
    }

    void IgnoreRagdollAgainst(Collider[] others)
    {
        if (others == null || _ragdollColliders == null)
            return;

        for (int i = 0; i < _ragdollColliders.Length; i++)
        {
            Collider ragdollCol = _ragdollColliders[i];
            if (ragdollCol == null)
                continue;

            for (int j = 0; j < others.Length; j++)
            {
                Collider otherCol = others[j];
                if (otherCol == null)
                    continue;

                Physics.IgnoreCollision(ragdollCol, otherCol, true);
            }
        }
    }

    void CacheHitCapsule()
    {
        CapsuleCollider col = GetComponent<CapsuleCollider>();
        if (col == null)
            return;

        float radius = Mathf.Max(0.1f, col.radius);
        float height = Mathf.Max(col.height, radius * 2f);
        float half = height * 0.5f;
        float centerY = col.center.y;
        _hitCapsuleRadius = radius + HitCapsuleRadiusPadding;
        _hitCapsuleBottom = centerY - half + radius;
        _hitCapsuleTop = centerY + half - radius;
    }

    void CacheHipsBody()
    {
        _hipsBody = null;
        if (_ragdollBodies == null)
            return;

        for (int i = 0; i < _ragdollBodies.Length; i++)
        {
            Rigidbody rb = _ragdollBodies[i];
            if (rb == null)
                continue;

            if (rb.gameObject.name.IndexOf("Hips") >= 0)
            {
                _hipsBody = rb;
                return;
            }
        }
    }

    void ApplyPendingExplosion()
    {
        if (!_hasPendingExplosion)
            return;

        _hasPendingExplosion = false;
        if (_ragdollBodies == null || _pendingExplosionForce <= 0f)
            return;

        float safeRadius = Mathf.Max(0.01f, _pendingExplosionRadius);
        // All ragdoll bones — Editor blast. Pose is already frozen so this no longer launches bind-pose overlap.
        for (int i = 0; i < _ragdollBodies.Length; i++)
        {
            Rigidbody rb = _ragdollBodies[i];
            if (rb == null || rb.isKinematic)
                continue;

            rb.maxDepenetrationVelocity = MaxDepenetrationSpeed;
            rb.AddExplosionForce(
                _pendingExplosionForce,
                _pendingExplosionPos,
                safeRadius,
                _pendingExplosionUpwards,
                ForceMode.Impulse);
        }
    }

    void ClampRagdollSpeed()
    {
        if (_ragdollBodies == null)
            return;

        float maxLinear = _isExplosionDeath ? MaxExplosionRagdollSpeed : MaxRagdollSpeed;
        float maxAngular = _isExplosionDeath ? MaxExplosionRagdollAngularSpeed : MaxRagdollSpeed;
        float maxLinearSq = maxLinear * maxLinear;
        float maxAngularSq = maxAngular * maxAngular;
        for (int i = 0; i < _ragdollBodies.Length; i++)
        {
            Rigidbody rb = _ragdollBodies[i];
            if (rb == null || rb.isKinematic)
                continue;

            Vector3 v = rb.linearVelocity;
            if (v.sqrMagnitude > maxLinearSq)
                rb.linearVelocity = v.normalized * maxLinear;

            Vector3 av = rb.angularVelocity;
            if (av.sqrMagnitude > maxAngularSq)
                rb.angularVelocity = av.normalized * maxAngular;
        }
    }

    static float DistanceSegmentSegment(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        Vector3 d1 = q1 - p1;
        Vector3 d2 = q2 - p2;
        Vector3 r = p1 - p2;
        float a = Vector3.Dot(d1, d1);
        float e = Vector3.Dot(d2, d2);
        float f = Vector3.Dot(d2, r);
        const float eps = 0.000001f;

        float s;
        float t;
        if (a <= eps && e <= eps)
            return Vector3.Distance(p1, p2);

        if (a <= eps)
        {
            s = 0f;
            t = Mathf.Clamp01(f / e);
        }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= eps)
            {
                t = 0f;
                s = Mathf.Clamp01(-c / a);
            }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denom = a * e - b * b;
                s = Mathf.Abs(denom) > eps ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                t = (b * s + f) / e;
                if (t < 0f)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else if (t > 1f)
                {
                    t = 1f;
                    s = Mathf.Clamp01((b - c) / a);
                }
            }
        }

        Vector3 c1 = p1 + d1 * s;
        Vector3 c2 = p2 + d2 * t;
        return Vector3.Distance(c1, c2);
    }

    void SetRagdollCollidersEnabled(bool enabled)
    {
        if (_ragdollColliders == null)
            return;

        for (int i = 0; i < _ragdollColliders.Length; i++)
        {
            if (_ragdollColliders[i] != null)
                _ragdollColliders[i].enabled = enabled;
        }
    }

    bool PrepareDissolveMaterials()
    {
        if (_dissolveShader == null)
            _dissolveShader = Shader.Find("Custom/EnemyDissolve");

        if (_dissolveShader == null || _renderers == null || _renderers.Length == 0)
            return false;

        SetRagdollKinematic(true);

        int skinnedCount = 0;
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] is SkinnedMeshRenderer)
                skinnedCount++;
        }

        if (skinnedCount > 0)
            return PrepareBakedDissolveMaterials(skinnedCount);

        return PrepareRendererDissolveMaterials();
    }

    bool PrepareBakedDissolveMaterials(int skinnedCount)
    {
        _bakedDissolveMeshes = new Mesh[skinnedCount];
        int meshIndex = 0;
        int materialCount = 0;

        for (int i = 0; i < _renderers.Length; i++)
        {
            SkinnedMeshRenderer smr = _renderers[i] as SkinnedMeshRenderer;
            if (smr == null)
                continue;

            Material[] shared = smr.sharedMaterials;
            if (shared != null)
                materialCount += shared.Length;
        }

        if (materialCount == 0)
            return false;

        _dissolveMaterials = new Material[materialCount];
        int writeIndex = 0;

        for (int i = 0; i < _renderers.Length; i++)
        {
            SkinnedMeshRenderer smr = _renderers[i] as SkinnedMeshRenderer;
            if (smr == null)
                continue;

            Mesh baked = new Mesh();
            baked.name = "ZombieDissolveBake";
            smr.BakeMesh(baked, true);
            _bakedDissolveMeshes[meshIndex++] = baked;

            GameObject bakeGo = new GameObject("DissolveMesh");
            bakeGo.layer = smr.gameObject.layer;
            bakeGo.transform.SetParent(smr.transform, false);
            bakeGo.transform.localPosition = Vector3.zero;
            bakeGo.transform.localRotation = Quaternion.identity;
            bakeGo.transform.localScale = Vector3.one;

            MeshFilter meshFilter = bakeGo.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = baked;

            MeshRenderer meshRenderer = bakeGo.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = smr.shadowCastingMode;
            meshRenderer.receiveShadows = smr.receiveShadows;

            Material[] shared = smr.sharedMaterials;
            if (shared == null || shared.Length == 0)
            {
                smr.enabled = false;
                continue;
            }

            Material[] instances = new Material[shared.Length];
            for (int m = 0; m < shared.Length; m++)
            {
                Material dissolveMat = new Material(_dissolveShader);
                CopySourceAppearance(shared[m], dissolveMat);
                dissolveMat.SetFloat(DissolveAmountId, 0f);
                dissolveMat.enableInstancing = false;
                instances[m] = dissolveMat;
                _dissolveMaterials[writeIndex++] = dissolveMat;
            }

            meshRenderer.materials = instances;
            smr.enabled = false;
        }

        return writeIndex > 0;
    }

    bool PrepareRendererDissolveMaterials()
    {
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
                dissolveMat.enableInstancing = false;
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

        // Keep the zombie's original texture/color
        if (source.HasProperty(BaseMapId))
        {
            dissolveMat.SetTexture(BaseMapId, source.GetTexture(BaseMapId));
            dissolveMat.SetTextureScale(BaseMapId, source.GetTextureScale(BaseMapId));
            dissolveMat.SetTextureOffset(BaseMapId, source.GetTextureOffset(BaseMapId));
        }
        else if (source.HasProperty(MainTexId))
        {
            dissolveMat.SetTexture(BaseMapId, source.GetTexture(MainTexId));
            dissolveMat.SetTextureScale(BaseMapId, source.GetTextureScale(MainTexId));
            dissolveMat.SetTextureOffset(BaseMapId, source.GetTextureOffset(MainTexId));
        }

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
        // Collider attached directly to root (not a ragdoll bone)
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
        // Overlapping bone colliders → PhysX pushes hard when ragdoll enables
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
        if (_dissolveMaterials != null)
        {
            for (int i = 0; i < _dissolveMaterials.Length; i++)
            {
                if (_dissolveMaterials[i] != null)
                    Destroy(_dissolveMaterials[i]);
            }
        }

        if (_bakedDissolveMeshes == null)
            return;

        for (int i = 0; i < _bakedDissolveMeshes.Length; i++)
        {
            if (_bakedDissolveMeshes[i] != null)
                Destroy(_bakedDissolveMeshes[i]);
        }
    }
}
