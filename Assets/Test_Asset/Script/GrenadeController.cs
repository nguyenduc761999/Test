using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Lựu đạn: bay vòng cung tới điểm aim, nổ khi chạm Plane.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class GrenadeController : MonoBehaviour
{
    [SerializeField] GameObject explosionVFX;
    [SerializeField] float rangeExplosion = 2.5f;

    /// <summary>Bán kính vùng nổ (aim AoE).</summary>
    public float RangeExplosion => Mathf.Max(0f, rangeExplosion);

    const float ExplosionForce = 32f;
    const float ExplosionUpwards = 1.2f;
    const float MinFlightTime = 0.45f;
    const float MaxFlightTime = 1.4f;
    const string PlaneName = "Plane";
    const int EnemyHitBufferSize = 32;

    Rigidbody _rb;
    LayerMask _enemyLayerMask;
    Collider[] _enemyHits;
    bool _exploded;
    bool _launched;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
            _rb = gameObject.AddComponent<Rigidbody>();

        SphereCollider col = GetComponent<SphereCollider>();
        if (col == null)
            col = gameObject.AddComponent<SphereCollider>();
        col.isTrigger = false;
        if (col.radius < 0.05f)
            col.radius = 0.15f;

        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.isKinematic = true;
        _rb.useGravity = true;

        _enemyLayerMask = LayerMask.GetMask("Enemy");
        _enemyHits = new Collider[EnemyHitBufferSize];
    }

    /// <summary>
    /// Phóng lựu theo quỹ đạo đạn đạo tới targetWorldPos.
    /// </summary>
    public void Init(Vector3 targetWorldPos)
    {
        if (_rb == null)
            _rb = GetComponent<Rigidbody>();

        Vector3 start = transform.position;
        Vector3 end = targetWorldPos;
        // Đáp trên mặt đất — giữ Y đích ổn định
        Vector3 velocity = CalculateLaunchVelocity(start, end);

        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.linearVelocity = velocity;
        _rb.angularVelocity = Random.insideUnitSphere * 4f;
        _launched = true;
        _exploded = false;

        // Fallback: nếu bay lâu không chạm Plane (lọt khe) → nổ gần điểm đích
        WatchLandingAsync(end);
    }

    async Awaitable WatchLandingAsync(Vector3 target)
    {
        float timeout = MaxFlightTime + 1.5f;
        float elapsed = 0f;
        while (!_exploded && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            Vector3 pos = transform.position;
            Vector3 flat = pos - target;
            flat.y = 0f;
            // Gần đích + sát đất → nổ
            if (flat.sqrMagnitude <= 0.35f * 0.35f && pos.y <= target.y + 0.35f)
            {
                Explode();
                return;
            }

            await Awaitable.NextFrameAsync();
        }

        if (!_exploded)
            Explode();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!_launched || _exploded || collision == null || collision.collider == null)
            return;

        if (!IsGroundPlane(collision.collider.transform))
            return;

        Explode();
    }

    static bool IsGroundPlane(Transform hit)
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

    void Explode()
    {
        if (_exploded)
            return;

        _exploded = true;

        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
        }

        Vector3 pos = transform.position;

        if (explosionVFX != null)
            Instantiate(explosionVFX, pos, Quaternion.identity);

        int hitCount = Physics.OverlapSphereNonAlloc(
            pos,
            RangeExplosion,
            _enemyHits,
            _enemyLayerMask);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = _enemyHits[i];
            if (hit == null)
                continue;

            EnemyController enemy = hit.GetComponentInParent<EnemyController>();
            if (enemy == null)
                continue;

            enemy.DieFromExplosion(pos, ExplosionForce, RangeExplosion, ExplosionUpwards);
            _enemyHits[i] = null;
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// Vận tốc ban đầu để rơi đúng điểm đích dưới gravity.
    /// </summary>
    static Vector3 CalculateLaunchVelocity(Vector3 start, Vector3 end)
    {
        Vector3 toTarget = end - start;
        Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
        float horizontal = flat.magnitude;
        // Thời gian bay tỷ lệ khoảng cách — cung rõ hơn khi ném xa
        float flightTime = Mathf.Clamp(
            Mathf.Lerp(MinFlightTime, MaxFlightTime, horizontal / 10f),
            MinFlightTime,
            MaxFlightTime);

        if (horizontal < 0.05f)
            flightTime = MinFlightTime;

        Vector3 gravity = Physics.gravity;
        Vector3 velocity = (toTarget - 0.5f * gravity * flightTime * flightTime) / flightTime;

        // Thêm loft nhẹ để luôn có vòng cung nhìn thấy
        if (velocity.y < 2f)
            velocity.y = 2f + horizontal * 0.15f;

        return velocity;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        rangeExplosion = Mathf.Max(0f, rangeExplosion);
    }

    void OnDrawGizmosSelected()
    {
        Handles.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        float newRange = Handles.RadiusHandle(Quaternion.identity, transform.position, Mathf.Max(0f, rangeExplosion));
        if (!Mathf.Approximately(newRange, rangeExplosion))
        {
            Undo.RecordObject(this, "Chỉnh Range Explosion");
            rangeExplosion = Mathf.Max(0f, newRange);
            EditorUtility.SetDirty(this);
        }

        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.15f);
        Gizmos.DrawSphere(transform.position, rangeExplosion);
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, rangeExplosion);
    }
#endif
}
