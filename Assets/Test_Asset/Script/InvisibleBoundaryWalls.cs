using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Creates 4 invisible BoxCollider walls around the ground Plane to keep the Player in bounds.
/// </summary>
public class InvisibleBoundaryWalls : MonoBehaviour
{
    [SerializeField] Transform _ground;
    [SerializeField] float _wallHeight = 10f;
    [SerializeField] float _wallThickness = 1f;

    const string WallsRootName = "InvisibleWalls";

    void Awake()
    {
        EnsureWalls();
    }

#if UNITY_EDITOR
    void Reset()
    {
        EnsureWalls();
    }

    void OnValidate()
    {
        EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            EnsureWalls();
        };
    }
#endif

    void EnsureWalls()
    {
        if (_ground == null)
        {
            var plane = transform.Find("Plane");
            if (plane != null)
                _ground = plane;
        }

        if (_ground == null || _wallHeight <= 0f || _wallThickness <= 0f)
            return;

        Bounds bounds;
        var groundCol = _ground.GetComponent<Collider>();
        if (groundCol != null)
        {
            bounds = groundCol.bounds;
        }
        else
        {
            var groundRenderer = _ground.GetComponent<Renderer>();
            if (groundRenderer == null)
                return;
            bounds = groundRenderer.bounds;
        }

        var root = transform.Find(WallsRootName);
        if (root == null)
        {
            var rootGo = new GameObject(WallsRootName);
            rootGo.transform.SetParent(transform, false);
            root = rootGo.transform;
        }

        float halfT = _wallThickness * 0.5f;
        float centerY = bounds.min.y + _wallHeight * 0.5f;
        float width = bounds.size.x;
        float depth = bounds.size.z;

        // Four sides: N/S stretch along X, E/W along Z (add thickness so corners meet).
        SetupWall(root, "Wall_North",
            new Vector3(bounds.center.x, centerY, bounds.max.z + halfT),
            new Vector3(width + _wallThickness, _wallHeight, _wallThickness));

        SetupWall(root, "Wall_South",
            new Vector3(bounds.center.x, centerY, bounds.min.z - halfT),
            new Vector3(width + _wallThickness, _wallHeight, _wallThickness));

        SetupWall(root, "Wall_East",
            new Vector3(bounds.max.x + halfT, centerY, bounds.center.z),
            new Vector3(_wallThickness, _wallHeight, depth));

        SetupWall(root, "Wall_West",
            new Vector3(bounds.min.x - halfT, centerY, bounds.center.z),
            new Vector3(_wallThickness, _wallHeight, depth));
    }

    static void SetupWall(Transform root, string name, Vector3 worldPos, Vector3 size)
    {
        Transform wallTf = root.Find(name);
        GameObject wallGo;
        if (wallTf == null)
        {
            wallGo = new GameObject(name);
            wallGo.transform.SetParent(root, false);
        }
        else
        {
            wallGo = wallTf.gameObject;
        }

        wallGo.transform.position = worldPos;
        wallGo.transform.rotation = Quaternion.identity;
        wallGo.transform.localScale = Vector3.one;

        var box = wallGo.GetComponent<BoxCollider>();
        if (box == null)
            box = wallGo.AddComponent<BoxCollider>();

        box.isTrigger = false;
        box.center = Vector3.zero;
        box.size = size;
    }
}
