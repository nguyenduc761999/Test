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

        // A: offset radius / avoidance / priority so agents differ
        ConfigureAgentAvoidance();
        // B: one slot around the player per enemy
        _chaseSlot = _nextChaseSlot++;

        // Agent drives position → disable root motion to avoid fighting it
        if (animController != null)
        {
            animController.applyRootMotion = false;
            _hasWalkParam = HasAnimatorParam(WalkHash, AnimatorControllerParameterType.Bool);
            _hasRunParam = HasAnimatorParam(RunHash, AnimatorControllerParameterType.Bool);
        }

        _playerController = FindFirstObjectByType<PlayerController>();
        if (_playerController != null)
            _player = _playerController.transform;

        if (_dissolveShader == null)
            _dissolveShader = Shader.Find("Custom/EnemyDissolve");

        // Health comes from EnemyConfig for the matching instance
        ApplyHealthFromConfig();
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
            return;

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
        _agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
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
        ApplyDie();

        if (_ragdollBodies == null || force <= 0f)
            return;

        float safeRadius = Mathf.Max(0.01f, radius);
        for (int i = 0; i < _ragdollBodies.Length; i++)
        {
            Rigidbody rb = _ragdollBodies[i];
            if (rb == null || rb.isKinematic)
                continue;

            rb.AddExplosionForce(force, explosionPos, safeRadius, upwardsModifier, ForceMode.Impulse);
        }
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

        Instantiate(hitVFX, transform.position, rotation);
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

        // Disable pathfinding before ragdoll
        if (_agent != null)
        {
            if (_agent.isOnNavMesh)
                _agent.isStopped = true;
            _agent.enabled = false;
        }

        if (_hasWalkParam && animController != null)
            animController.SetBool(WalkHash, false);
        if (_hasRunParam && animController != null)
            animController.SetBool(RunHash, false);

        // Disable Animator so physics drives the bones
        if (animController != null)
            animController.enabled = false;

        // Disable the root collider (avoids hitting bone capsules → launch)
        if (_rootColliders != null)
        {
            for (int i = 0; i < _rootColliders.Length; i++)
            {
                if (_rootColliders[i] != null)
                    _rootColliders[i].enabled = false;
            }
        }

        // Enable ragdoll (clear leftover velocity before physics)
        SetRagdollKinematic(false);

        // Wait for ragdoll, then dissolve → Destroy
        DissolveAfterDieAsync();
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

        // Keep the zombie's original texture/color
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
        // Destroy material instances to avoid leaks
        if (_dissolveMaterials == null)
            return;

        for (int i = 0; i < _dissolveMaterials.Length; i++)
        {
            if (_dissolveMaterials[i] != null)
                Destroy(_dissolveMaterials[i]);
        }
    }
}
