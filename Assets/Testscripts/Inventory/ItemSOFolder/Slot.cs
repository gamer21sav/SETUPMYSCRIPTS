using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class Slot : MonoBehaviour
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
            amountTxt.textStyle = "";
        }
    }

}
