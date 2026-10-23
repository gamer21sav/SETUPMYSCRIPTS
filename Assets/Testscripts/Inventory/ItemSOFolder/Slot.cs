using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class Slot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public bool hovering;

    private ItemSO heldItem;
    private int itemAmount;
    private Image iconImage;
    private TextMeshProUGUI amountTxt;

    private void Awake()
    {
        iconImage = GetComponentInChildren<Image>();
        amountTxt = GetComponentInChildren<TextMeshProUGUI>();
    }
    public ItemSO GetItem()
    {
        return heldItem;
    }
    public int GetAmount()
    {
        return itemAmount;
    }
    public void SetItem(ItemSO item, int amount = 1)
    {
        heldItem = item;
        itemAmount = amount;
        UpdateSlot();
    }
    public void UpdateSlot()
    {
        if (heldItem != null)
        {
            iconImage.enabled = true;
            iconImage.sprite = heldItem.Icon;
            amountTxt.text = itemAmount.ToString();
        }
        else
        {
            iconImage.enabled = false;
            amountTxt.text = "";

        }
    }
    public int AddAmount(int amountToAdd)
    {
        itemAmount += amountToAdd;
        UpdateSlot();
        return itemAmount;
    }
    public int RemoveAmount(int amountToRemove)
    {
        itemAmount -= amountToRemove;
        if (itemAmount <= 0)
        {
            ClearSlot();
        }
        else
        {
            UpdateSlot();
        }

        return itemAmount;
    }
    public void ClearSlot()
    {
        heldItem = null;
        itemAmount = 0;
        UpdateSlot();
    }
    public bool hasItem()
    {
        return heldItem != null;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovering = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovering = false;
    }
}
