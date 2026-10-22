using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Opens/closes a UI panel with I. Attach to an ALWAYS-ACTIVE object outside
/// BagRoot. The panel may start inactive. Works with either Unity input backend.
/// This only changes visibility; it does not pause gameplay or change the cursor.
/// No inventory, hotbar, movement, or networking dependencies.
/// </summary>
[DisallowMultipleComponent]
public sealed class BagUIToggle : MonoBehaviour
{
    [SerializeField] private GameObject bagRoot;
    [SerializeField] private bool startOpen = false;

    public bool IsOpen => bagRoot != null && bagRoot.activeSelf;

    private void Awake()
    {
        if (bagRoot == null)
        {
            Debug.LogError("BagUIToggle: assign BagRoot in the Inspector.", this);
            enabled = false;
            return;
        }

        if (transform == bagRoot.transform || transform.IsChildOf(bagRoot.transform))
        {
            Debug.LogError("BagUIToggle must be on an always-active object OUTSIDE BagRoot.", this);
            enabled = false;
        }
    }

    private void Start()
    {
        SetOpen(startOpen);
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.iKey.wasPressedThisFrame)
            Toggle();
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.I))
            Toggle();
#endif
    }

    public void Toggle() => SetOpen(!IsOpen);
    public void Open() => SetOpen(true);
    public void Close() => SetOpen(false);

    public void SetOpen(bool open)
    {
        if (bagRoot != null)
            bagRoot.SetActive(open);
    }
}
