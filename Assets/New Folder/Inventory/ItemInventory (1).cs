using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Completely standalone GameObject inventory. Stores any world GameObject,
/// not just weapons. No Pickup, Gun, RotateWeapon, input, settings, UI or
/// networking classes are required. Use TryAdd / SelectSlot / DropSelected.
/// UI systems (hotbar, bag, crafting) may listen to Changed.
/// </summary>
[DisallowMultipleComponent]
public sealed class ItemInventory : MonoBehaviour
{
    [Header("Capacity")]
    [SerializeField, Min(1)] private int capacity = 8;
    [SerializeField] private bool equipNewItems = true;

    [Header("Optional Item Locations - Created At Runtime If Missing")]
    [SerializeField] private Transform storageRoot;
    [SerializeField] private Transform equipMount;
    [SerializeField] private Vector3 autoEquipMountPosition = new Vector3(0.35f, 1.25f, 0.55f);

    [Header("Drop")]
    [SerializeField, Min(0f)] private float dropForward = 1.25f;
    [SerializeField, Min(0f)] private float dropUp = 0.75f;
    [SerializeField] private bool makeDroppedObjectsDynamic = true;

    [Header("Unity Inspector Event (Optional)")]
    public UnityEvent OnChanged = new UnityEvent();

    private sealed class Entry
    {
        public GameObject Item;
        public Collider[] Colliders;
        public bool[] EnabledColliders;
        public Rigidbody Body;
        public bool WasKinematic;
        public bool UsedGravity;
        public Vector3 OriginalLocalScale;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private int selectedIndex = -1;

    public event Action Changed;
    public event Action<GameObject> Added;
    public event Action<GameObject> Removed;
    public event Action<GameObject> Equipped;
    public event Action<GameObject> Unequipped;

    public int Count { get { return entries.Count; } }
    public int Capacity { get { return Mathf.Max(1, capacity); } }
    public bool IsFull { get { return Count >= Capacity; } }
    public int SelectedIndex { get { return selectedIndex; } }
    public GameObject EquippedItem { get { return GetItem(selectedIndex); } }
    public Transform EquipMount { get { EnsureRoots(); return equipMount; } }

    private void Awake() { EnsureRoots(); }

    private void EnsureRoots()
    {
        if (storageRoot == null)
        {
            GameObject storage = new GameObject("Inventory Stored Items (Runtime)");
            storageRoot = storage.transform;
            storageRoot.SetParent(transform, false);
        }
        if (equipMount == null)
        {
            GameObject mount = new GameObject("Inventory Equip Mount (Runtime - Assign Hand Socket)");
            equipMount = mount.transform;
            equipMount.SetParent(transform, false);
            equipMount.localPosition = autoEquipMountPosition;
        }
    }

    public GameObject GetItem(int slot)
    {
        return slot >= 0 && slot < entries.Count ? entries[slot].Item : null;
    }

    public string GetItemName(int slot)
    {
        GameObject item = GetItem(slot);
        return item != null ? item.name.Replace("(Clone)", "").Trim() : "";
    }

    public bool Contains(GameObject item)
    {
        if (item == null) return false;
        for (int i = 0; i < entries.Count; i++) if (entries[i].Item == item) return true;
        return false;
    }

    /// <summary>Returns false for full inventory, duplicate, or invalid object.</summary>
    public bool TryAdd(GameObject item)
    {
        if (item == null || item == gameObject || IsFull || Contains(item)) return false;
        if (item.transform.IsChildOf(transform)) return false;
        EnsureRoots();
        Entry entry = new Entry();
        entry.Item = item;
        entry.OriginalLocalScale = item.transform.localScale;
        entry.Colliders = item.GetComponentsInChildren<Collider>(true);
        entry.EnabledColliders = new bool[entry.Colliders.Length];
        for (int i = 0; i < entry.Colliders.Length; i++)
        {
            entry.EnabledColliders[i] = entry.Colliders[i].enabled;
            entry.Colliders[i].enabled = false;
        }
        entry.Body = item.GetComponent<Rigidbody>();
        if (entry.Body != null)
        {
            entry.WasKinematic = entry.Body.isKinematic;
            entry.UsedGravity = entry.Body.useGravity;
            entry.Body.isKinematic = true;
            entry.Body.useGravity = false;
        }
        item.transform.SetParent(storageRoot, false);
        item.SetActive(false);
        entries.Add(entry);
        if (Added != null) Added(item);
        if (equipNewItems) SelectSlot(entries.Count - 1);
        else NotifyChanged();
        return true;
    }

    /// <summary>Selects ANY collected item, not specifically a weapon.</summary>
    public bool SelectSlot(int index)
    {
        if (index < -1 || index >= entries.Count) return false;
        EnsureRoots();
        GameObject old = EquippedItem;
        if (old != null && old != GetItem(index) && Unequipped != null) Unequipped(old);
        selectedIndex = index;
        for (int i = 0; i < entries.Count; i++)
        {
            GameObject item = entries[i].Item;
            if (item == null) continue;
            bool selected = i == selectedIndex;
            item.transform.SetParent(selected ? equipMount : storageRoot, false);
            if (selected)
            {
                item.transform.localPosition = Vector3.zero;
                item.transform.localRotation = Quaternion.identity;
                item.transform.localScale = entries[i].OriginalLocalScale;
            }
            item.SetActive(selected);
        }
        GameObject now = EquippedItem;
        if (now != null && now != old && Equipped != null) Equipped(now);
        NotifyChanged();
        return true;
    }

    public void NextSlot()
    {
        if (Count > 0) SelectSlot((selectedIndex + 1 + Count) % Count);
    }

    public void PreviousSlot()
    {
        if (Count > 0) SelectSlot((selectedIndex - 1 + Count) % Count);
    }

    public bool DropSelected()
    {
        return DropSlot(selectedIndex);
    }

    public bool DropSlot(int slot)
    {
        if (slot < 0 || slot >= entries.Count) return false;
        Entry entry = entries[slot];
        GameObject item = entry.Item;
        if (item == null)
        {
            entries.RemoveAt(slot);
            selectedIndex = -1;
            if (entries.Count > 0) SelectSlot(Mathf.Min(slot, entries.Count - 1));
            else NotifyChanged();
            return true;
        }

        int oldSelection = selectedIndex;
        if (item == EquippedItem && Unequipped != null) Unequipped(item);
        entries.RemoveAt(slot);
        selectedIndex = -1;
        item.transform.SetParent(null, true);
        item.transform.position = transform.position + transform.forward * dropForward + Vector3.up * dropUp;
        item.transform.localScale = entry.OriginalLocalScale;
        item.SetActive(true);

        for (int i = 0; i < entry.Colliders.Length; i++)
            if (entry.Colliders[i] != null) entry.Colliders[i].enabled = entry.EnabledColliders[i];
        if (entry.Body != null)
        {
            entry.Body.isKinematic = makeDroppedObjectsDynamic ? false : entry.WasKinematic;
            entry.Body.useGravity = makeDroppedObjectsDynamic ? true : entry.UsedGravity;
        }
        // A no-Rigidbody item is still a valid stored/dropped object. It will stay put.
        if (Removed != null) Removed(item);
        if (entries.Count > 0)
        {
            int nextSelection = oldSelection == slot ? Mathf.Min(slot, entries.Count - 1)
                : oldSelection > slot ? oldSelection - 1 : oldSelection;
            SelectSlot(nextSelection);
        }
        else NotifyChanged();
        return true;
    }

    // Future Bag UI can read GetItem(i)/Count, and use these public methods.
    private void NotifyChanged()
    {
        if (Changed != null) Changed();
        OnChanged.Invoke();
    }
}
