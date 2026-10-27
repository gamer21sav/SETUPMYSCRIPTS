// 150-line compact edition. Same public API and serialized Inspector fields.
// Dependencies: existing Pickup and PlayerControlSettings; Unity uGUI.
// Icon components are cached until a slot receives a different object.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
[DisallowMultipleComponent]
public class SimpleInventory : MonoBehaviour
{
    public bool isBag; [Min(1)] public int slots = 8;
    public Pickup pickup; public RectTransform hotbar; public RectTransform bagPanelParent;
    public Transform handHolder; public Transform bagHolder; public Behaviour[] pauseWhileOpen; public GameObject bagPanelPrefab;
    public Sprite bagIcon; private class Item
    {
        public GameObject obj; public Collider[] colliders;
        public bool[] colliderEnabled; public Rigidbody[] bodies; public bool[] gravity, kinematic; public Vector3 scale;
    }
    private class Cell
    {
        public SimpleInventory owner; public int index; public GameObject go;
        public Image background, icon; public Text label; public GameObject cachedObject; public SimpleInventory cachedBag;
        public SpriteRenderer cachedSprite;
    }
    private Item[] contents; private Transform storage; private int selected;
    private SimpleInventory equippedBag; private GameObject panel; private bool open; private bool oldPickupInput, oldCursorVisible; private CursorLockMode oldCursorLock;
    private readonly List<Behaviour> paused = new List<Behaviour>(); private readonly List<Cell> hotbarCells = new List<Cell>();
    private readonly List<Cell> bagCells = new List<Cell>(); private Cell dragging; private Item draggedItem; private GameObject ghost; private SimpleInventory viewer;
    private static readonly Dictionary<GameObject, SimpleInventory> owners = new Dictionary<GameObject, SimpleInventory>();
    private Func<GameObject, bool> receive, canReceive; private Func<bool> drop; public event Action Changed;
    public GameObject SelectedItem => GetItem(selected); public SimpleInventory EquippedBag => equippedBag;
    void Awake() { Initialize(); }
    void Initialize()
    {
        if (contents != null) return;
        contents = new Item[Mathf.Max(1, slots)]; storage = new GameObject("Stored Items").transform;
        storage.SetParent(transform, false);
    }
    void OnEnable()
    {
        if (isBag) return; if (pickup == null) pickup = GetComponent<Pickup>();
        if (pickup == null) { Debug.LogError("Assign Pickup on this Player.", this); return; }
        receive = Store; canReceive = CanStore; drop = Drop;
        pickup.PickupHandler = receive; pickup.CanPickupItem = canReceive; pickup.DropHandler = drop;
    }
    void Start()
    {
        if (isBag) return; if (hotbar != null) BuildCells(hotbar, this, hotbarCells, true);
        else Debug.LogError("Assign the player's Hotbar UI object.", this); Refresh();
    }
    void OnDisable()
    {
        if (isBag) return; SetOpen(false); CancelDrag(); if (pickup == null) return; if (pickup.PickupHandler == receive) pickup.PickupHandler = null;
        if (pickup.CanPickupItem == canReceive) pickup.CanPickupItem = null; if (pickup.DropHandler == drop) pickup.DropHandler = null;
    }
    void OnDestroy()
    {
        if (!isBag && panel != null) Destroy(panel); if (contents != null) foreach (var item in contents)
            if (item != null && !ReferenceEquals(item.obj, null)) owners.Remove(item.obj);
    }
    public GameObject GetItem(int index)
    {
        Initialize();
        return index >= 0 && index < contents.Length && contents[index] != null ? contents[index].obj : null;
    }
    int EmptySlot()
    {
        Initialize(); for (int i = 0; i < contents.Length; i++) if (GetItem(i) == null) return i;
        return -1;
    }
    public bool CanStore(GameObject obj)
    {
        return obj != null && obj != gameObject && !owners.ContainsKey(obj) &&
        !obj.transform.IsChildOf(transform) && !transform.IsChildOf(obj.transform) && EmptySlot() >= 0;
    }
    bool OwnPart(Transform part, Transform root)
    {
        for (var t = part; t != null && t != root; t = t.parent)
            if (owners.ContainsKey(t.gameObject)) return false; return true;
    }
    public bool Store(GameObject obj)
    {
        if (!CanStore(obj)) return false; int index = EmptySlot(); var cs = new List<Collider>(); var bs = new List<Rigidbody>();
        foreach (var c in obj.GetComponentsInChildren<Collider>(true)) if (OwnPart(c.transform, obj.transform)) cs.Add(c);
        foreach (var b in obj.GetComponentsInChildren<Rigidbody>(true)) if (OwnPart(b.transform, obj.transform)) bs.Add(b);
        var item = new Item
        {
            obj = obj,
            scale = obj.transform.lossyScale,
            colliders = cs.ToArray(),
            colliderEnabled = new bool[cs.Count],
            bodies = bs.ToArray(),
            gravity = new bool[bs.Count],
            kinematic = new bool[bs.Count]
        };
        for (int i = 0; i < cs.Count; i++) { item.colliderEnabled[i] = cs[i].enabled; cs[i].enabled = false; }
        for (int i = 0; i < bs.Count; i++)
        { item.gravity[i] = bs[i].useGravity; item.kinematic[i] = bs[i].isKinematic; bs[i].isKinematic = true; bs[i].useGravity = false; }
        contents[index] = item; owners[obj] = this; if (!isBag) selected = index; Refresh(); return true;
    }
    void Refresh()
    {
        Initialize(); for (int i = 0; i < contents.Length; i++)
        {
            var obj = GetItem(i); if (obj == null) continue; bool worn = !isBag && equippedBag != null && obj == equippedBag.gameObject;
            Transform mount = worn ? bagHolder : (!isBag && i == selected ? handHolder : null);
            var parent = mount != null ? mount : storage; if (obj.transform.parent != parent) obj.transform.SetParent(parent, true);
            if (mount != null) { obj.transform.localPosition = Vector3.zero; obj.transform.localRotation = Quaternion.identity; }
            if (obj.activeSelf != (mount != null)) obj.SetActive(mount != null);
        }
        foreach (var cell in hotbarCells) Draw(cell); foreach (var cell in bagCells) Draw(cell); if (viewer != null) viewer.DrawBag(); Changed?.Invoke();
    }
    void DrawBag() { foreach (var cell in bagCells) Draw(cell); }
    public void Select(int index)
    { if (!isBag && index >= 0 && index < contents.Length) { selected = index; Refresh(); } }
    public void Equip()
    {
        if (isBag) return; var obj = SelectedItem; var bag = obj != null ? obj.GetComponent<SimpleInventory>() : null;
        if (bag == null || !bag.isBag) return; bool same = bag == equippedBag; Unequip(); if (same) return;
        if (bagPanelParent == null) { Debug.LogError("Assign Bag Panel Parent on Player.", this); return; }
        bag.Initialize(); equippedBag = bag; bag.viewer = this; panel = bag.bagPanelPrefab != null ? Instantiate(bag.bagPanelPrefab, bagPanelParent, false)
        : new GameObject("Bag Panel", typeof(RectTransform), typeof(Image));
        if (bag.bagPanelPrefab == null)
        {
            panel.transform.SetParent(bagPanelParent, false); ((RectTransform)panel.transform).sizeDelta = new Vector2(510, 240);
            panel.GetComponent<Image>().color = new Color(.08f, .09f, .12f, .95f);
        }
        RectTransform root = null;
        foreach (var t in panel.GetComponentsInChildren<RectTransform>(true)) if (t.name == "BagSlotsEmptyGameObject") { root = t; break; }
        if (root == null)
        { var go = new GameObject("BagSlotsEmptyGameObject", typeof(RectTransform)); go.transform.SetParent(panel.transform, false); root = (RectTransform)go.transform; }
        BuildCells(root, bag, bagCells, false); panel.SetActive(false); Refresh();
    }
    public void Unequip()
    {
        SetOpen(false); CancelDrag(); if (equippedBag != null) equippedBag.viewer = null; equippedBag = null;
        if (panel != null) { panel.SetActive(false); Destroy(panel); }
        panel = null; bagCells.Clear(); Refresh();
    }
    public void TogglePanel() { SetOpen(!open); }
    void SetOpen(bool value)
    {
        if (isBag || value == open || (value && panel == null)) return; open = value; CancelDrag(); if (panel != null) panel.SetActive(value);
        if (value)
        {
            oldCursorLock = Cursor.lockState; oldCursorVisible = Cursor.visible;
            oldPickupInput = pickup != null && pickup.InputAllowed; if (pickup != null) pickup.SetInputAllowed(false); paused.Clear();
            if (pauseWhileOpen != null) foreach (var b in pauseWhileOpen) if (b != null && b.enabled && b != this && b != pickup && !(b is SimpleInventory) &&
            !(b is EventSystem) && !(b is BaseInputModule) && !(b is GraphicRaycaster)) { paused.Add(b); b.enabled = false; }
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        else
        {
            if (pickup != null) pickup.SetInputAllowed(oldPickupInput); foreach (var b in paused) if (b != null) b.enabled = true;
            paused.Clear(); Cursor.lockState = oldCursorLock; Cursor.visible = oldCursorVisible;
        }
    }
    public bool Drop()
    {
        if (isBag || SelectedItem == null) return false;
        var item = contents[selected]; if (equippedBag != null && item.obj == equippedBag.gameObject) Unequip();
        contents[selected] = null; owners.Remove(item.obj); item.obj.transform.SetParent(null, true);
        item.obj.transform.position = transform.position + transform.forward * 1.5f + Vector3.up * .6f; item.obj.transform.localScale = item.scale; item.obj.SetActive(true);
        for (int i = 0; i < item.colliders.Length; i++) if (item.colliders[i] != null) item.colliders[i].enabled = item.colliderEnabled[i];
        for (int i = 0; i < item.bodies.Length; i++) if (item.bodies[i] != null)
        { item.bodies[i].isKinematic = item.kinematic[i]; item.bodies[i].useGravity = item.gravity[i]; }
        Refresh(); return true;
    }
    bool Worn(Item item) { return item != null && equippedBag != null && item.obj == equippedBag.gameObject; }
    void Move(Cell target)
    {
        if (!open || dragging == null || !dragging.go.activeInHierarchy || !target.go.activeInHierarchy || dragging.owner.GetItem(dragging.index) == null ||
        dragging.owner.contents[dragging.index] != draggedItem || target == dragging) return;
        var a = dragging.owner.contents[dragging.index]; var b = target.owner.contents[target.index];
        if (Worn(a) || Worn(b) || target.owner.transform.IsChildOf(a.obj.transform) || (b != null && b.obj != null && dragging.owner.transform.IsChildOf(b.obj.transform))) return;
        var source = dragging.owner; source.contents[dragging.index] = b; target.owner.contents[target.index] = a;
        owners[a.obj] = target.owner; if (b != null && b.obj != null) owners[b.obj] = source; CancelDrag(); source.Refresh(); if (target.owner != source) target.owner.Refresh();
    }
    void BeginDrag(Cell cell, BaseEventData data)
    {
        var pointer = data as PointerEventData; CancelDrag();
        if (!open || pointer == null || pointer.button != PointerEventData.InputButton.Left || cell.owner.GetItem(cell.index) == null) return;
        if (Worn(cell.owner.contents[cell.index])) return; dragging = cell; draggedItem = cell.owner.contents[cell.index];
        var canvas = cell.go.GetComponentInParent<Canvas>(); if (canvas == null) { CancelDrag(); return; }
        ghost = new GameObject("Dragged Item", typeof(RectTransform), typeof(Image)); ghost.transform.SetParent(canvas.rootCanvas.transform, false);
        var image = ghost.GetComponent<Image>(); image.sprite = cell.icon.sprite; image.color = new Color(1, 1, 1, .65f); image.raycastTarget = false;
        ((RectTransform)ghost.transform).sizeDelta = new Vector2(64, 64); Drag(data);
    }
    void Drag(BaseEventData data)
    {
        if (ghost == null) return;
        var e = data as PointerEventData; if (e == null) return; var canvas = ghost.GetComponentInParent<Canvas>();
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)canvas.transform, e.position, camera, out Vector3 p)) ghost.transform.position = p;
    }
    void CancelDrag() { dragging = null; draggedItem = null; if (ghost != null) Destroy(ghost); ghost = null; }
    void BuildCells(RectTransform root, SimpleInventory owner, List<Cell> list, bool horizontal)
    {
        list.Clear(); owner.Initialize(); var children = new List<GameObject>();
        foreach (Transform t in root) if (t is RectTransform) children.Add(t.gameObject); if (children.Count == 0)
        {
            var grid = root.GetComponent<GridLayoutGroup>(); if (grid == null) grid = root.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(90, 90); grid.spacing = new Vector2(8, 8);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = horizontal ? owner.contents.Length : Mathf.Min(5, owner.contents.Length);
            int columns = grid.constraintCount; root.sizeDelta = new Vector2(columns * 98 - 8, Mathf.CeilToInt((float)owner.contents.Length / columns) * 98 - 8);
            for (int i = 0; i < owner.contents.Length; i++) { var go = new GameObject("Slot " + (i + 1), typeof(RectTransform), typeof(Image)); go.transform.SetParent(root, false); children.Add(go); }
        }
        if (children.Count < owner.contents.Length) Debug.LogError("Add more UI slots, or reduce Slots to " + children.Count, root);
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); for (int i = 0; i < children.Count; i++)
        {
            var go = children[i]; go.SetActive(i < owner.contents.Length); if (i >= owner.contents.Length) continue;
            var bg = go.GetComponent<Image>(); if (bg == null) bg = go.AddComponent<Image>(); bg.raycastTarget = true;
            foreach (var graphic in go.GetComponentsInChildren<Graphic>(true)) if (graphic != bg) graphic.raycastTarget = false;
            var image = new GameObject("Inventory Icon", typeof(RectTransform), typeof(Image)); image.transform.SetParent(go.transform, false);
            var ir = (RectTransform)image.transform; ir.anchorMin = new Vector2(.15f, .3f); ir.anchorMax = new Vector2(.85f, .95f); ir.offsetMin = ir.offsetMax = Vector2.zero;
            var icon = image.GetComponent<Image>(); icon.preserveAspect = true; icon.raycastTarget = false;
            var text = new GameObject("Inventory Label", typeof(RectTransform), typeof(Text)); text.transform.SetParent(go.transform, false);
            var tr = (RectTransform)text.transform; tr.anchorMin = Vector2.zero; tr.anchorMax = new Vector2(1, .35f); tr.offsetMin = tr.offsetMax = Vector2.zero;
            var label = text.GetComponent<Text>(); label.font = font; label.fontSize = 14; label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
            var cell = new Cell { owner = owner, index = i, go = go, background = bg, icon = icon, label = label }; list.Add(cell);
            var trigger = go.GetComponent<EventTrigger>(); if (trigger == null) trigger = go.AddComponent<EventTrigger>(); AddEvent(trigger, EventTriggerType.BeginDrag, e => BeginDrag(cell, e));
            AddEvent(trigger, EventTriggerType.Drag, Drag); AddEvent(trigger, EventTriggerType.EndDrag, e => CancelDrag()); AddEvent(trigger, EventTriggerType.Drop, e => Move(cell));
            AddEvent(trigger, EventTriggerType.PointerClick, e => { var p = e as PointerEventData; if (!owner.isBag && p != null && p.button == PointerEventData.InputButton.Left && !p.dragging) Select(cell.index); });
        }
    }
    static void AddEvent(EventTrigger trigger, EventTriggerType type, UnityAction<BaseEventData> action)
    { var entry = new EventTrigger.Entry { eventID = type }; entry.callback.AddListener(action); trigger.triggers.Add(entry); }
    void Draw(Cell cell)
    {
        var obj = cell.owner.GetItem(cell.index); if (!ReferenceEquals(cell.cachedObject, obj))
        {
            cell.cachedObject = obj;
            cell.cachedBag = obj != null ? obj.GetComponent<SimpleInventory>() : null;
            cell.cachedSprite = obj != null ? obj.GetComponentInChildren<SpriteRenderer>(true) : null;
        }
        var bag = cell.cachedBag; var sprite = cell.cachedSprite;
        cell.icon.sprite = bag != null && bag.bagIcon != null ? bag.bagIcon : (sprite != null ? sprite.sprite : null); cell.icon.enabled = cell.icon.sprite != null;
        cell.label.text = (cell.owner.isBag ? "" : (cell.index + 1) + " ") + (obj != null ? obj.name.Replace("(Clone)", "") : "");
        if (obj != null && equippedBag != null && obj == equippedBag.gameObject) cell.label.text += " [Equipped]";
        cell.background.color = !cell.owner.isBag && cell.index == selected ? new Color(.6f, .4f, .08f, 1) : new Color(.1f, .13f, .19f, .95f);
    }
    void Update()
    {
        if (isBag) return; if (equippedBag == null && panel != null) Unequip();
        if (PlayerControlSettings.GameplayBlocked) return; bool equip = PlayerControlSettings.Pressed(PlayerControlSettings.Control.EquipBag);
        bool toggle = PlayerControlSettings.Pressed(PlayerControlSettings.Control.Inventory); int number = -1; float scroll = 0; for (int i = 0; i < 9; i++)
            if (PlayerControlSettings.Pressed((PlayerControlSettings.Control)((int)PlayerControlSettings.Control.Slot1 + i))) number = i;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null) scroll = Mouse.current.scroll.ReadValue().y;
#elif ENABLE_LEGACY_INPUT_MANAGER
        scroll=Input.GetAxis("Mouse ScrollWheel");
#endif
        if (toggle) TogglePanel(); if (open) return; if (number >= 0) Select(number); if (scroll > .01f) Select((selected + contents.Length - 1) % contents.Length);
        if (scroll < -.01f) Select((selected + 1) % contents.Length); if (equip) Equip();
    }
}
#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(SimpleInventory))]
public class SimpleInventoryInspector : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update(); UnityEditor.EditorGUILayout.PropertyField(serializedObject.FindProperty("isBag"));
        UnityEditor.EditorGUILayout.PropertyField(serializedObject.FindProperty("slots"));
        bool bag = serializedObject.FindProperty("isBag").boolValue; string[] fields = bag ? new[] { "bagPanelPrefab", "bagIcon" } :
        new[] { "pickup", "hotbar", "bagPanelParent", "handHolder", "bagHolder", "pauseWhileOpen" };
        foreach (var field in fields) UnityEditor.EditorGUILayout.PropertyField(serializedObject.FindProperty(field), true);
        UnityEditor.EditorGUILayout.HelpBox(bag ? "This object owns its stored items. Panel Prefab is optional." :
        "Bindings are configured in PlayerControlSettings. Wheel selects hotbar slots.", UnityEditor.MessageType.Info); serializedObject.ApplyModifiedProperties();
    }
}
#endif