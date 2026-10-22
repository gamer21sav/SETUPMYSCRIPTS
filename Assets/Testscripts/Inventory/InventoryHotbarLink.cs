using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// OPTIONAL bridge. Only rename/import as .cs when BOTH ItemInventory.cs
/// and ItemHotbar.cs are installed. No dependency on Pickup or other scripts.
/// Also supplies OPTIONAL number keys, scroll wheel, and gamepad bumper cycling.
/// </summary>
[DisallowMultipleComponent]
public sealed class InventoryHotbarLink : MonoBehaviour
{
    [SerializeField] private ItemInventory inventory;
    [SerializeField] private ItemHotbar hotbar;
    [SerializeField] private bool readSlotInputs = true;
    [SerializeField] private bool gamepadBumperCycle = true;
    public bool InputAllowed { get; set; } = true;

    private void Awake()
    {
        if (inventory == null) inventory = GetComponent<ItemInventory>();
        if (inventory == null) inventory = GetComponentInParent<ItemInventory>();
        if (hotbar == null) hotbar = GetComponentInChildren<ItemHotbar>(true);
        if (hotbar == null && inventory != null)
            hotbar = inventory.GetComponentInChildren<ItemHotbar>(true);
        if (inventory == null || hotbar == null)
            Debug.LogWarning("InventoryHotbarLink needs ItemInventory and ItemHotbar references.", this);
    }

    private void OnEnable()
    {
        if (inventory != null) inventory.Changed += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (inventory != null) inventory.Changed -= Refresh;
    }

    private void Refresh()
    {
        if (inventory == null || hotbar == null) return;
        string[] names = new string[inventory.Count];
        for (int i = 0; i < names.Length; i++) names[i] = inventory.GetItemName(i);
        hotbar.SetItems(names, inventory.SelectedIndex);
    }

    private void Update()
    {
        if (!InputAllowed || !readSlotInputs || inventory == null || inventory.Count == 0) return;
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame) inventory.SelectSlot(0);
            else if (kb.digit2Key.wasPressedThisFrame) inventory.SelectSlot(1);
            else if (kb.digit3Key.wasPressedThisFrame) inventory.SelectSlot(2);
            else if (kb.digit4Key.wasPressedThisFrame) inventory.SelectSlot(3);
            else if (kb.digit5Key.wasPressedThisFrame) inventory.SelectSlot(4);
            else if (kb.digit6Key.wasPressedThisFrame) inventory.SelectSlot(5);
            else if (kb.digit7Key.wasPressedThisFrame) inventory.SelectSlot(6);
            else if (kb.digit8Key.wasPressedThisFrame) inventory.SelectSlot(7);
            else if (kb.digit9Key.wasPressedThisFrame) inventory.SelectSlot(8);
        }
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll > 0.01f) inventory.PreviousSlot();
            else if (scroll < -0.01f) inventory.NextSlot();
        }
        Gamepad pad = Gamepad.current;
        if (gamepadBumperCycle && pad != null)
        {
            if (pad.leftShoulder.wasPressedThisFrame) inventory.PreviousSlot();
            if (pad.rightShoulder.wasPressedThisFrame) inventory.NextSlot();
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        KeyCode[] numbers = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3,
            KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7,
            KeyCode.Alpha8, KeyCode.Alpha9 };
        for (int i = 0; i < numbers.Length; i++)
            if (Input.GetKeyDown(numbers[i])) { inventory.SelectSlot(i); break; }
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll > 0.01f) inventory.PreviousSlot();
        else if (scroll < -0.01f) inventory.NextSlot();
#endif
    }
}
