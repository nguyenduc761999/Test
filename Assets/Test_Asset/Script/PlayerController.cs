using UnityEngine;

public class PlayerController : MonoBehaviour
{
    GameObject _characterModel;
    FixedJoystick _fixedJoystick;
    Animator _animController;

    const float MoveSpeed = 5f;
    const float RotateSpeed = 10f;

    void Awake()
    {
        // Lấy model con của Character khi vào Play
        Transform character = transform.Find("Character");
        if (character != null && character.childCount > 0)
            _characterModel = character.GetChild(0).gameObject;

        // Cache Animator của characterModel
        if (_characterModel != null)
            _animController = _characterModel.GetComponent<Animator>();

        // Cache FixedJoystick trên scene
        _fixedJoystick = FindFirstObjectByType<FixedJoystick>();
    }

    void Update()
    {
        if (_fixedJoystick == null || _characterModel == null)
            return;

        float horizontal = _fixedJoystick.Horizontal;
        float vertical = _fixedJoystick.Vertical;
        bool isRunning = horizontal != 0f || vertical != 0f;

        // Bật/tắt bool Run theo trạng thái joystick
        if (_animController != null)
            _animController.SetBool("Run", isRunning);

        if (!isRunning)
            return;

        Vector3 moveDirection = new Vector3(horizontal, 0f, vertical);
        Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
        _characterModel.transform.rotation = Quaternion.Slerp(
            _characterModel.transform.rotation,
            targetRotation,
            RotateSpeed * Time.deltaTime);
        transform.Translate(moveDirection * MoveSpeed * Time.deltaTime, Space.World);
    }
}
