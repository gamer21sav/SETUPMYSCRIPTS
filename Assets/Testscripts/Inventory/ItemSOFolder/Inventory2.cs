using UnityEngine;
using System.Collections.Generic;
using Unity.VisualScripting;
public class Inventory2 : MonoBehaviour
{
    public GameObject hotbarObject;
    public GameObject InventorySlotParent;
    private List<Slot> InventorySlots = new List<Slot>();
    private List<Slot> hotbarSlots = new List<Slot>();
    private List<Slot> allSlots = new List<Slot>();
    private void Awake()
    {
        InventorySlots.AddRange(InventorySlotParent.GetComponentsInChildren<Slot>());
        hotbarSlots.AddRange(hotbarObject.GetComponentsInChildren<Slot>());
        allSlots.AddRange(InventorySlots);
        allSlots.AddRange(hotbarSlots);

    }
    // Update is called once per frame
    void Update()
    {

    }
    public void AddItem(ItemSO itemToAdd, int amount)
    {
        int remaining = amount;
        foreach (Slot slot in allSlots)
        {
            if (slot.hasItem() && slot.GetItem() == itemToAdd)
            {
                int currentAmount = slot.GetAmount();
                int maxStack = itemToAdd.maxStackSize;
                if (currentAmount < maxStack)
                {
                    int spaceLeft = maxStack - currentAmount;
                    int amountToAdd = Mathf.Min(spaceLeft, remaining);
                    slot.SetItem(itemToAdd, currentAmount + amountToAdd);
                    remaining -= amountToAdd;

                    if (remaining <= 0)
                    {
                        return;
                    }
                }
            }
        }
        foreach (Slot slot in allSlots)
        {
            if (!slot.hasItem())
            {
                int amountToPlace = Mathf.Min(itemToAdd.maxStackSize, remaining);
                slot.SetItem(itemToAdd, amountToPlace);
                if (remaining <= 0)
                    return;
            }
        }
        if (remaining > 0)
        {
            Debug.Log("Inventory full " + remaining + " of " + itemToAdd.itemName);
        }
    }
}
