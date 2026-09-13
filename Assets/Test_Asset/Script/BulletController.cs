using UnityEngine;

/// <summary>
/// Bullet flies in a straight line from the muzzle; on Enemy hit, applies damage then Destroy.
/// Hits use the enemy transform capsule along that path (PhysX triggers tunnel on mobile).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class BulletController : MonoBehaviour
{
    const float LifetimeSeconds = 5f;
    const float ColliderRadius = 0.15f;
    const float MaxStepDistance = 0.25f;
    const float MaxDeltaTime = 0.03333334f;

    float _speed;
    float _damage;
    Vector3 _direction = Vector3.forward;
    bool _initialized;
    bool _hit;

    Rigidbody _rigidbody;
    SphereCollider _collider;

    void Awake()
    {
        EnsurePhysicsComponents();
    }

    /// <summary>
    /// Sets damage / speed / flight direction from WeaponConfig at spawn.
    /// Direction is fixed at spawn — the bullet does not home in flight.
    /// </summary>
    public void Init(float damage, float speed, Vector3 direction)
    {
        EnsurePhysicsComponents();

        _damage = Mathf.Max(0f, damage);
        _speed = Mathf.Max(0f, speed);
        _direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        transform.rotation = Quaternion.LookRotation(_direction);
        _initialized = true;
        _hit = false;

        DestroyAfterLifetimeAsync();
    }

    void Update()
    {
        if (!_initialized || _hit)
            return;

        float dt = Time.deltaTime;
        if (dt > MaxDeltaTime)
            dt = MaxDeltaTime;

        float remaining = _speed * dt;
        if (remaining <= 0f)
            return;

        Vector3 origin = transform.position;
        while (remaining > 0f)
        {
            float step = remaining > MaxStepDistance ? MaxStepDistance : remaining;
            Vector3 next = origin + _direction * step;
            if (TryHitAlong(origin, next))
                return;

            origin = next;
            remaining -= step;
        }

        transform.position = origin;
        if (_rigidbody != null)
            _rigidbody.position = origin;
    }

    bool TryHitAlong(Vector3 from, Vector3 to)
    {
        int count = EnemyController.ActiveEnemyCount;
        for (int i = 0; i < count; i++)
        {
            EnemyController enemy = EnemyController.GetActiveEnemy(i);
            if (enemy == null || enemy.die)
                continue;

            if (!enemy.HitByShot(from, to, ColliderRadius))
                continue;

            return ApplyHit(enemy);
        }

        return false;
    }

    bool ApplyHit(EnemyController enemy)
    {
        if (_hit || enemy == null)
            return false;

        _hit = true;
        enemy.TakeDamage(_damage, transform);
        Destroy(gameObject);
        return true;
    }

    void EnsurePhysicsComponents()
    {
        if (_rigidbody == null)
            _rigidbody = GetComponent<Rigidbody>();
        if (_rigidbody == null)
            _rigidbody = gameObject.AddComponent<Rigidbody>();

        _rigidbody.isKinematic = true;
        _rigidbody.useGravity = false;
        _rigidbody.detectCollisions = false;
        _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        _rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;

        if (_collider == null)
            _collider = GetComponent<SphereCollider>();
        if (_collider == null)
            _collider = gameObject.AddComponent<SphereCollider>();

        _collider.isTrigger = true;
        _collider.enabled = false;
        _collider.radius = ColliderRadius;
    }

    async Awaitable DestroyAfterLifetimeAsync()
    {
        await Awaitable.WaitForSecondsAsync(LifetimeSeconds);
        if (this == null || _hit)
            return;

        Destroy(gameObject);
    }
}
