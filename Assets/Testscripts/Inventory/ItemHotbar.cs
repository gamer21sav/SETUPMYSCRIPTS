using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Standalone, presentation-only item hotbar. No Pickup, ItemInventory, weapon,
/// input, TMP or settings dependencies. Supports horizontal/vertical layout,
/// readable item names, and OPTIONAL sprites supplied by any inventory adapter.
/// Attach to a UI RectTransform under a Canvas.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class ItemHotbar : MonoBehaviour
{
    public enum LayoutDirection { Horizontal, Vertical }

    [Header("Layout")]
    [SerializeField] private LayoutDirection direction = LayoutDirection.Horizontal;
    [SerializeField, Min(1)] private int minimumVisibleSlots = 5;
    [SerializeField, Min(30f)] private float slotWidth = 90f;
    [SerializeField, Min(30f)] private float slotHeight = 90f;
    [SerializeField, Min(0f)] private float spacing = 8f;
    [SerializeField] private Vector2 padding = new Vector2(6f, 6f);
    [SerializeField] private TextAnchor slotAlignment = TextAnchor.MiddleCenter;

    [Header("Appearance")]
    [SerializeField] private Color normalColor = new Color(0.09f, 0.11f, 0.17f, 0.85f);
    [SerializeField] private Color selectedColor = new Color(0.95f, 0.7f, 0.1f, 0.94f);
    [SerializeField] private Color emptyColor = new Color(0.08f, 0.08f, 0.08f, 0.55f);
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private bool showItemNames = true;
    [SerializeField] private bool showSlotNumbers = true;
    [SerializeField, Range(10, 28)] private int labelFontSize = 15;
    [SerializeField, Range(0.2f, 1f)] private float spriteSizeFraction = 0.65f;

    private string[] itemNames = new string[0];
    private Sprite[] itemSprites = new Sprite[0];
    private int selected = -1;
    private RectTransform generatedRoot;
    private Image[] slotImages = new Image[0];
    private Image[] slotIcons = new Image[0];
    private Text[] slotTexts = new Text[0];
    private int slotsCreated;
    private LayoutDirection createdDirection;

    public LayoutDirection Direction => direction;
    public int VisibleSlotCount => slotsCreated;

    private void Awake() { Rebuild(); }

    /// <summary>Compatible with InventoryHotbarLink's existing two-argument call.</summary>
    public void SetItems(string[] names, int selectedIndex)
    {
        SetItems(names, null, selectedIndex);
    }

    /// <summary>Optional icon API for a later bag/hotbar adapter; no inventory type is required.</summary>
    public void SetItems(string[] names, Sprite[] icons, int selectedIndex)
    {
        itemNames = names != null ? (string[])names.Clone() : new string[0];
        itemSprites = icons != null ? (Sprite[])icons.Clone() : new Sprite[0];
        selected = selectedIndex;
        Rebuild();
    }

    public void Clear() { SetItems(null, null, -1); }

    /// <summary>Call after changing orientation/size via script at runtime.</summary>
    public void SetDirection(LayoutDirection value)
    {
        if (direction == value) return;
        direction = value;
        Rebuild();
    }

    public void Refresh() { Rebuild(); }

    private void EnsureRoot()
    {
        if (generatedRoot != null && createdDirection == direction) return;
        if (generatedRoot != null)
        {
            // Old children are ignored; Destroy occurs at end of frame.
            generatedRoot.gameObject.SetActive(false);
            Destroy(generatedRoot.gameObject);
            generatedRoot = null;
        }

        GameObject root = new GameObject("Generated Item Hotbar", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        generatedRoot = root.GetComponent<RectTransform>();
        generatedRoot.anchorMin = new Vector2(0.5f, 0.5f);
        generatedRoot.anchorMax = new Vector2(0.5f, 0.5f);
        generatedRoot.pivot = new Vector2(0.5f, 0.5f);
        generatedRoot.anchoredPosition = Vector2.zero;

        HorizontalOrVerticalLayoutGroup layout;
        if (direction == LayoutDirection.Horizontal)
            layout = root.AddComponent<HorizontalLayoutGroup>();
        else
            layout = root.AddComponent<VerticalLayoutGroup>();

        layout.childAlignment = slotAlignment;
        layout.childForceExpandHeight = false; // Uses our preferred slot height.
        layout.childForceExpandWidth = false;  // Uses our preferred slot width.
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.spacing = spacing;
        layout.padding = new RectOffset(Mathf.RoundToInt(padding.x), Mathf.RoundToInt(padding.x),
            Mathf.RoundToInt(padding.y), Mathf.RoundToInt(padding.y));

        createdDirection = direction;
        slotsCreated = 0;
    }

    private void Rebuild()
    {
        EnsureRoot();
        int needed = Mathf.Max(minimumVisibleSlots, itemNames.Length);
        bool rebuildChildren = slotsCreated != needed;
        if (rebuildChildren)
        {
            for (int i = generatedRoot.childCount - 1; i >= 0; i--)
            {
                GameObject child = generatedRoot.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            slotImages = new Image[needed];
            slotIcons = new Image[needed];
            slotTexts = new Text[needed];

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int i = 0; i < needed; i++)
            {
                GameObject slot = new GameObject("Slot " + (i + 1),
                    typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                slot.transform.SetParent(generatedRoot, false);
                LayoutElement element = slot.GetComponent<LayoutElement>();
                element.preferredWidth = slotWidth;
                element.preferredHeight = slotHeight;
                Image background = slot.GetComponent<Image>();
                background.raycastTarget = false;
                slotImages[i] = background;

                GameObject iconObject = new GameObject("Optional Item Icon", typeof(RectTransform), typeof(Image));
                iconObject.transform.SetParent(slot.transform, false);
                RectTransform iconRect = iconObject.GetComponent<RectTransform>();
                iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.anchoredPosition = Vector2.zero;
                Image icon = iconObject.GetComponent<Image>();
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                slotIcons[i] = icon;

                GameObject textObject = new GameObject("Slot Label", typeof(RectTransform), typeof(Text));
                textObject.transform.SetParent(slot.transform, false);
                RectTransform textRect = textObject.GetComponent<RectTransform>();
                textRect.anchorMin = new Vector2(0f, 0f);
                textRect.anchorMax = new Vector2(1f, 0.33f);
                textRect.offsetMin = new Vector2(3f, 2f);
                textRect.offsetMax = new Vector2(-3f, 0f);
                Text label = textObject.GetComponent<Text>();
                label.font = font;
                label.fontSize = labelFontSize;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 9;
                label.resizeTextMaxSize = labelFontSize;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = textColor;
                label.raycastTarget = false;
                slotTexts[i] = label;
            }
            slotsCreated = needed;
        }

        HorizontalOrVerticalLayoutGroup layoutGroup = generatedRoot.GetComponent<HorizontalOrVerticalLayoutGroup>();
        layoutGroup.spacing = spacing;
        layoutGroup.childAlignment = slotAlignment;
        layoutGroup.padding.left = layoutGroup.padding.right = Mathf.RoundToInt(padding.x);
        layoutGroup.padding.top = layoutGroup.padding.bottom = Mathf.RoundToInt(padding.y);

        for (int i = 0; i < slotsCreated; i++)
        {
            bool hasName = i < itemNames.Length && !string.IsNullOrWhiteSpace(itemNames[i]);
            bool hasSprite = i < itemSprites.Length && itemSprites[i] != null;
            bool occupied = hasName || hasSprite;
            slotImages[i].color = occupied ? (selected == i ? selectedColor : normalColor) : emptyColor;
            slotImages[i].GetComponent<LayoutElement>().preferredWidth = slotWidth;
            slotImages[i].GetComponent<LayoutElement>().preferredHeight = slotHeight;

            slotIcons[i].sprite = hasSprite ? itemSprites[i] : null;
            slotIcons[i].enabled = hasSprite;
            RectTransform iconRect = (RectTransform)slotIcons[i].transform;
            float size = Mathf.Min(slotWidth, slotHeight) * spriteSizeFraction;
            iconRect.sizeDelta = new Vector2(size, size);

            slotTexts[i].fontSize = labelFontSize;
            slotTexts[i].resizeTextMaxSize = labelFontSize;
            slotTexts[i].color = textColor;
            string prefix = showSlotNumbers ? (i + 1).ToString() : "";
            string name = showItemNames && hasName ? itemNames[i] : "";
            slotTexts[i].text = name.Length > 0 && prefix.Length > 0 ? prefix + ": " + name
                : name.Length > 0 ? name : prefix;
        }

        float width = direction == LayoutDirection.Horizontal
            ? needed * slotWidth + (needed - 1) * spacing + 2f * padding.x
            : slotWidth + 2f * padding.x;
        float height = direction == LayoutDirection.Vertical
            ? needed * slotHeight + (needed - 1) * spacing + 2f * padding.y
            : slotHeight + 2f * padding.y;
        generatedRoot.sizeDelta = new Vector2(width, height);
    }
}
