using UnityEngine;

/// <summary>
/// Bullet flies in a straight line; on Enemy hit, applies damage then Destroy.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class BulletController : MonoBehaviour
{
    const float LifetimeSeconds = 5f;
    const float ColliderRadius = 0.08f;

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

        transform.position += _direction * _speed * Time.deltaTime;
    }

    void OnTriggerEnter(Collider other)
    {
        if (_hit || other == null)
            return;

        EnemyController enemy = other.GetComponentInParent<EnemyController>();
        if (enemy == null)
            return;

        _hit = true;
        // Pass the bullet transform so the Enemy can spawn hitVFX opposite the flight direction
        enemy.TakeDamage(_damage, transform);
        Destroy(gameObject);
    }

    void EnsurePhysicsComponents()
    {
        if (_rigidbody == null)
            _rigidbody = GetComponent<Rigidbody>();
        if (_rigidbody == null)
            _rigidbody = gameObject.AddComponent<Rigidbody>();

        _rigidbody.isKinematic = true;
        _rigidbody.useGravity = false;
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        if (_collider == null)
            _collider = GetComponent<SphereCollider>();
        if (_collider == null)
            _collider = gameObject.AddComponent<SphereCollider>();

        _collider.isTrigger = true;
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
