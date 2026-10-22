using UnityEngine;
using UnityEngine.EventSystems;
public class PlayerCursorLock : MonoBehaviour
{
    [SerializeField] private bool lockOnStart = true;
    [SerializeField] private KeyCode unlockKey = KeyCode.Escape;

    private void Start()
    {
        if (lockOnStart)
            LockCursor();
    }

    private void Update()
    {
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            if (Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject())
                LockCursor();
        }
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(unlockKey))
            UnlockCursor();
#elif ENABLE_INPUT_SYSTEM
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            UnlockCursor();
            
#endif
    }

    public void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
