using System;
using UnityEngine;

/// <summary>
/// OPTIONAL bridge. Do not rename/import this .txt as .cs unless BOTH Pickup.cs
/// and ItemInventory.cs are in your project. Each core script works without it.
/// </summary>
[DisallowMultipleComponent]
public sealed class PickupInventoryLink : MonoBehaviour
{
    [SerializeField] private Pickup pickup;
    [SerializeField] private ItemInventory inventory;
    private Func<GameObject, bool> accept;
    private Func<bool> drop;
    private Func<GameObject, bool> eligibility;

    private void Awake()
    {
        if (pickup == null) pickup = GetComponent<Pickup>();
        if (pickup == null) pickup = GetComponentInParent<Pickup>();
        if (inventory == null) inventory = GetComponent<ItemInventory>();
        if (inventory == null) inventory = GetComponentInParent<ItemInventory>();
        if (pickup == null || inventory == null)
            Debug.LogWarning("PickupInventoryLink needs Pickup AND ItemInventory references.", this);
    }

    private void OnEnable()
    {
        if (pickup == null || inventory == null) return;
        accept = inventory.TryAdd;
        drop = inventory.DropSelected;
        eligibility = delegate(GameObject item) { return item != null && !inventory.IsFull && !inventory.Contains(item); };
        pickup.PickupHandler = accept;
        pickup.DropHandler = drop;
        pickup.CanPickupItem = eligibility;
    }

    private void OnDisable()
    {
        if (pickup == null) return;
        if (pickup.PickupHandler == accept) pickup.PickupHandler = null;
        if (pickup.DropHandler == drop) pickup.DropHandler = null;
        if (pickup.CanPickupItem == eligibility) pickup.CanPickupItem = null;
    }
}
