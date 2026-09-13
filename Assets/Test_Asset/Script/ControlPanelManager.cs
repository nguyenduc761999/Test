using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// ControlPanel: switch guns from Inventory + Arena-of-Valor-style grenade aim/throw.
/// </summary>
public class ControlPanelManager : MonoBehaviour
{
    [SerializeField] Button switchWeaponBtn;
    [SerializeField] Button grenadeBtn;
    [SerializeField] UserConfig _userConfig;
    [SerializeField] WeaponConfig _weaponConfig;
    [SerializeField] SoundConfig _soundConfig;
    [SerializeField] PlayerController _playerController;

    Image _iconGun;
    EventTrigger _grenadeTrigger;
    bool _grenadeDragging;
    Vector2 _grenadeDragOrigin;
    int _grenadePointerId = int.MinValue;
    Camera _uiCamera;

    const string SwitchWeaponBtnName = "SwitchWeaponBtn";
    const string GrenadeBtnName = "GrenadeBtn";
    const string IconGunName = "IconGun";
    /// <summary>Dragging this many pixels = throw at max rangeGrenade (Arena of Valor style).</summary>
    const float MaxDragPixels = 140f;
#if UNITY_EDITOR
    const string UserConfigAssetPath = "Assets/Test_Asset/Config/UserConfig.asset";
    const string WeaponConfigAssetPath = "Assets/Test_Asset/Config/WeaponConfig.asset";
    const string SoundConfigAssetPath = "Assets/Test_Asset/Config/SoundConfig.asset";
#endif

    void Awake()
    {
        if (_playerController == null)
            _playerController = FindFirstObjectByType<PlayerController>();

        CacheIconGun();
        CacheUiCamera();

        if (_soundConfig != null)
            SoundManager.Ensure(_soundConfig);

        if (switchWeaponBtn != null)
            switchWeaponBtn.onClick.AddListener(SwitchWeapon);

        WireGrenadeBtn();
    }

    void Start()
    {
        RefreshIconGun();
    }

    void OnDestroy()
    {
        if (switchWeaponBtn != null)
            switchWeaponBtn.onClick.RemoveListener(SwitchWeapon);

        UnwireGrenadeBtn();
    }

    void WireGrenadeBtn()
    {
        if (grenadeBtn == null)
            return;

        // Button/Selectable sends Cancel on drag, which cancels aim. Disable Button, keep Image raycast.
        grenadeBtn.transition = Selectable.Transition.None;
        grenadeBtn.enabled = false;

        Image img = grenadeBtn.GetComponent<Image>();
        if (img != null)
            img.raycastTarget = true;

        _grenadeTrigger = grenadeBtn.GetComponent<EventTrigger>();
        if (_grenadeTrigger == null)
            _grenadeTrigger = grenadeBtn.gameObject.AddComponent<EventTrigger>();

        _grenadeTrigger.triggers.Clear();
        // Do not bind Cancel — Selectable/Button Cancel drops the drag as soon as you start dragging
        AddTrigger(EventTriggerType.PointerDown, OnGrenadePointerDown);
        AddTrigger(EventTriggerType.InitializePotentialDrag, OnGrenadeInitializePotentialDrag);
        AddTrigger(EventTriggerType.BeginDrag, OnGrenadeDrag);
        AddTrigger(EventTriggerType.Drag, OnGrenadeDrag);
        AddTrigger(EventTriggerType.EndDrag, OnGrenadePointerUp);
        AddTrigger(EventTriggerType.PointerUp, OnGrenadePointerUp);
    }

    void UnwireGrenadeBtn()
    {
        if (_grenadeTrigger != null)
            _grenadeTrigger.triggers.Clear();
        _grenadeDragging = false;
        _grenadePointerId = int.MinValue;
    }

    void AddTrigger(EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> callback)
    {
        EventTrigger.Entry entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(callback);
        _grenadeTrigger.triggers.Add(entry);
    }

    void OnGrenadeInitializePotentialDrag(BaseEventData eventData)
    {
        // Allow drag immediately, skip EventSystem's default threshold
        if (eventData is PointerEventData pointer)
            pointer.useDragThreshold = false;
    }

    void OnGrenadePointerDown(BaseEventData eventData)
    {
        if (_playerController == null || !(eventData is PointerEventData pointer))
            return;

        CacheUiCamera();
        _grenadeDragOrigin = GetGrenadeBtnScreenCenter();
        _grenadeDragging = true;
        _grenadePointerId = pointer.pointerId;
        pointer.useDragThreshold = false;

        _playerController.BeginGrenadeAim();
        SoundManager.Instance?.PlayButton(SoundConfig.ButtonClick);
        ApplyDragAim(pointer.position);
    }

    void OnGrenadeDrag(BaseEventData eventData)
    {
        if (!_grenadeDragging || _playerController == null || !(eventData is PointerEventData pointer))
            return;

        if (pointer.pointerId != _grenadePointerId)
            return;

        ApplyDragAim(pointer.position);
    }

    void OnGrenadePointerUp(BaseEventData eventData)
    {
        if (!_grenadeDragging)
            return;

        if (eventData is PointerEventData pointer && pointer.pointerId != _grenadePointerId)
            return;

        // Update aim one last time, then throw
        if (eventData is PointerEventData upPointer)
            ApplyDragAim(upPointer.position);

        EndGrenadeDrag();
    }

    void ApplyDragAim(Vector2 screenPos)
    {
        Vector2 delta = screenPos - _grenadeDragOrigin;
        _playerController.UpdateGrenadeAimByDrag(delta, MaxDragPixels);
    }

    void EndGrenadeDrag()
    {
        if (!_grenadeDragging)
            return;

        _grenadeDragging = false;
        _grenadePointerId = int.MinValue;
        _playerController?.ConfirmGrenadeThrow();
    }

    Vector2 GetGrenadeBtnScreenCenter()
    {
        if (grenadeBtn == null)
            return Vector2.zero;

        RectTransform rt = grenadeBtn.transform as RectTransform;
        if (rt == null)
            return RectTransformUtility.WorldToScreenPoint(_uiCamera, grenadeBtn.transform.position);

        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Vector3 center = (corners[0] + corners[2]) * 0.5f;
        return RectTransformUtility.WorldToScreenPoint(_uiCamera, center);
    }

    void CacheUiCamera()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            _uiCamera = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
        else
            _uiCamera = null;
    }

    /// <summary>
    /// Cycles WeaponUse → Save → IconGun → Player swaps gun.
    /// </summary>
    void SwitchWeapon()
    {
        SoundManager.Instance?.PlayButton(SoundConfig.ButtonClick);

        if (_userConfig == null || _userConfig.Inventory == null)
            return;

        int count = _userConfig.Inventory.Count;
        if (count <= 1)
            return;

        _userConfig.WeaponUse = (_userConfig.WeaponUse + 1) % count;
        _userConfig.ClampWeaponUse();
        _userConfig.Save();

        RefreshIconGun();
        _playerController?.EquipInventoryWeapon();
        SoundManager.Instance?.PlaySfx(SoundConfig.SfxWeaponSwitch);
    }

    /// <summary>
    /// Assigns the IconGun sprite from the currently WeaponUse weapon.
    /// </summary>
    void RefreshIconGun()
    {
        if (_iconGun == null)
            CacheIconGun();

        if (_iconGun == null)
            return;

        Sprite icon = null;
        if (_userConfig != null && _userConfig.Inventory != null && _userConfig.Inventory.Count > 0
            && _weaponConfig != null)
        {
            _userConfig.ClampWeaponUse();
            GameObject gunPrefab = _userConfig.Inventory[_userConfig.WeaponUse];
            WeaponEntry entry = _weaponConfig.FindByGunPrefab(gunPrefab);
            if (entry != null)
                icon = entry.Icon;
        }

        _iconGun.sprite = icon;
        _iconGun.enabled = icon != null;
    }

    void CacheIconGun()
    {
        _iconGun = null;
        if (switchWeaponBtn == null)
            return;

        Transform iconTf = FindChildByName(switchWeaponBtn.transform, IconGunName);
        if (iconTf != null)
            _iconGun = iconTf.GetComponent<Image>();
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
        if (switchWeaponBtn == null)
        {
            Transform btnTf = transform.Find(SwitchWeaponBtnName);
            if (btnTf == null)
                btnTf = FindChildByName(transform, SwitchWeaponBtnName);
            if (btnTf != null)
                switchWeaponBtn = btnTf.GetComponent<Button>();
        }

        if (grenadeBtn == null)
        {
            Transform btnTf = transform.Find(GrenadeBtnName);
            if (btnTf == null)
                btnTf = FindChildByName(transform, GrenadeBtnName);
            if (btnTf != null)
                grenadeBtn = btnTf.GetComponent<Button>();
        }

        if (_userConfig == null)
            _userConfig = AssetDatabase.LoadAssetAtPath<UserConfig>(UserConfigAssetPath);
        if (_weaponConfig == null)
            _weaponConfig = AssetDatabase.LoadAssetAtPath<WeaponConfig>(WeaponConfigAssetPath);
        if (_soundConfig == null)
            _soundConfig = AssetDatabase.LoadAssetAtPath<SoundConfig>(SoundConfigAssetPath);
    }
#endif
}
