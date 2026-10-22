using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Generic, standalone player pickup component. No Gun, RotateWeapon, inventory,
/// camera rig, settings manager, TMP or UI package required.
///
/// A pickupable object needs a Collider and, by default, a Rigidbody on its root.
/// This avoids accidentally collecting the terrain. For non-Rigidbody objects,
/// set Eligibility=LayerOnly and use a dedicated Pickup layer instead.
///
/// Optional receivers assign PickupHandler, returning true only when accepted.
/// With no receiver this script has its OWN small equipped/stored-item fallback.
/// </summary>
[DisallowMultipleComponent]
public sealed class Pickup : MonoBehaviour
{
    public enum EligibilityMode { RigidbodyOnly, LayerOnly, RigidbodyOrLayer }
    public enum InteractionKey { F, E, R }

    [Header("Candidate Filtering - No Item Script Required")]
    [SerializeField] private EligibilityMode eligibility = EligibilityMode.RigidbodyOnly;
    [Tooltip("Select your Pickup layer for LayerOnly, or filter by layer when using a Rigidbody.")]
    [SerializeField] private LayerMask pickupLayers = ~0;
    [SerializeField, Min(0.2f)] private float range = 2.8f;
    [SerializeField, Min(0f)] private float searchHeight = 1.0f;
    [SerializeField, Min(0.01f)] private float scanSeconds = 0.075f;
    [SerializeField] private bool requireLooking = true;
    [SerializeField, Range(0.03f, 0.75f)] private float screenAimTolerance = 0.33f;
    [SerializeField] private bool requireClearPath = true;
    [SerializeField] private LayerMask sightBlockers = ~0;

    [Header("Input - Optional; Can Call TryPickup() From Any Input Adapter")]
    [SerializeField] private bool readLocalInput = true;
    [SerializeField] private InteractionKey interactKey = InteractionKey.F;
    [SerializeField] private bool acceptGamepadWestButton = true;
    [SerializeField] private bool readDropKey = true;

    [Header("Auto-Discovered References")]
    [SerializeField] private Transform playerRoot;
    [SerializeField] private Camera viewCamera;

    [Header("Object-Anchored Label - Built-in TextMesh, No TMP/uGUI")]
    [SerializeField] private bool showWorldPrompt = true;
    [SerializeField, Min(0f)] private float labelHeight = 0.22f;
    [SerializeField, Min(0.001f)] private float nearLabelScale = 0.18f;
    [SerializeField, Min(0.001f)] private float farLabelScale = 0.075f;
    [SerializeField, Min(0f)] private float labelSmoothness = 12f;
    [SerializeField] private Color labelColor = Color.white;

    [Header("Standalone Fallback Storage/Equip - Used Only Without A Receiver")]
    [SerializeField] private bool allowStandaloneStorage = true;
    [SerializeField, Min(1)] private int maxStandaloneItems = 8;
    [SerializeField] private Transform equipMount;
    [SerializeField, Min(0f)] private float dropDistance = 1.25f;

    // An OPTIONAL adapter may set these without Pickup knowing about inventories.
    // Return false from PickupHandler when the inventory is full/rejects the item.
    public Func<GameObject, bool> PickupHandler { get; set; }
    // Optional filter. Example: ignore items when ItemInventory is full.
    public Func<GameObject, bool> CanPickupItem { get; set; }
    public Func<bool> DropHandler { get; set; }
    public event Action<GameObject> ItemPickedUp;
    public event Action<GameObject> FocusChanged;
    public bool InputAllowed { get; private set; } = true;
    public GameObject FocusedItem { get { return focusedItem; } }
    public bool HasTarget { get { return focusedItem != null; } }
    public int StandaloneItemCount { get { return localItems.Count; } }

    private struct Stored
    {
        public GameObject Item;
        public Collider[] Colliders;
        public bool[] ColliderEnabled;
        public Rigidbody Rigidbody;
        public bool WasKinematic;
        public bool UsedGravity;
        public Vector3 OriginalScale;
    }

    private readonly List<Stored> localItems = new List<Stored>();
    private readonly Collider[] candidates = new Collider[96];
    private readonly RaycastHit[] lineHits = new RaycastHit[32];
    private Transform storageRoot;
    private GameObject focusedItem;
    private Collider focusedCollider;
    private GameObject promptObject;
    private TextMesh promptText;
    private float labelScale;
    private float nextScanTime;
    private int equippedLocalIndex = -1;
    private bool warnedBufferFull;

    private void Awake()
    {
        if (playerRoot == null) playerRoot = transform;
        if (viewCamera == null) viewCamera = playerRoot.GetComponentInChildren<Camera>(true);
    }

    private void OnEnable() { nextScanTime = 0f; }
    private void OnDisable() { SetFocus(null, null); HidePrompt(); }
    private void OnDestroy()
    {
        if (promptObject != null) Destroy(promptObject);
    }

    public void SetInputAllowed(bool allowed)
    {
        InputAllowed = allowed;
        if (!allowed) SetFocus(null, null);
    }

    public void SetCamera(Camera camera) { viewCamera = camera; nextScanTime = 0f; }
    public void SetPlayerRoot(Transform root) { playerRoot = root != null ? root : transform; nextScanTime = 0f; }

    private void Update()
    {
        if (!InputAllowed) { HidePrompt(); return; }
        if (Time.time >= nextScanTime)
        {
            nextScanTime = Time.time + Mathf.Max(0.01f, scanSeconds);
            FindFocus();
        }
        if (!readLocalInput) return;
        if (ReadPickupPressed()) TryPickup();
        if (readDropKey && ReadDropPressed()) TryDrop();
    }

    private void LateUpdate()
    {
        if (!InputAllowed || !showWorldPrompt || focusedItem == null || focusedCollider == null)
        {
            HidePrompt();
            return;
        }
        Camera camera = ResolveCamera();
        if (camera == null) { HidePrompt(); return; }
        BuildPrompt();
        if (!promptObject.activeSelf) promptObject.SetActive(true);
        Vector3 top = focusedCollider.bounds.center + Vector3.up *
            (focusedCollider.bounds.extents.y + labelHeight);
        promptObject.transform.SetPositionAndRotation(top, camera.transform.rotation);
        float itemDistance = Vector3.Distance(SearchOrigin(), focusedCollider.ClosestPoint(SearchOrigin()));
        float t = Mathf.Clamp01(itemDistance / Mathf.Max(0.1f, range));
        float targetScale = Mathf.Lerp(nearLabelScale, farLabelScale, t);
        float lerp = 1f - Mathf.Exp(-labelSmoothness * Time.unscaledDeltaTime);
        labelScale = Mathf.Lerp(labelScale, targetScale, lerp);
        promptObject.transform.localScale = Vector3.one * labelScale;
    }

    private Vector3 SearchOrigin()
    {
        Transform root = playerRoot != null ? playerRoot : transform;
        return root.position + Vector3.up * searchHeight;
    }

    private Camera ResolveCamera()
    {
        if (viewCamera != null && viewCamera.isActiveAndEnabled) return viewCamera;
        Transform root = playerRoot != null ? playerRoot : transform;
        Camera[] cameras = root.GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i].isActiveAndEnabled) { viewCamera = cameras[i]; return viewCamera; }
        }
        return null;
    }

    private void FindFocus()
    {
        GameObject result;
        Collider hitbox;
        FindClosest(out result, out hitbox);
        SetFocus(result, hitbox);
    }

    private void FindClosest(out GameObject result, out Collider chosen)
    {
        result = null;
        chosen = null;
        Transform root = playerRoot != null ? playerRoot : transform;
        Camera camera = ResolveCamera();
        if (requireLooking && camera == null) return;

        Vector3 origin = SearchOrigin();
        int count = Physics.OverlapSphereNonAlloc(origin, range, candidates,
            pickupLayers, QueryTriggerInteraction.Collide);
        if (count == candidates.Length && !warnedBufferFull)
        {
            Debug.LogWarning("Pickup query buffer filled: reduce nearby objects or enlarge the buffer in Pickup.cs.", this);
            warnedBufferFull = true;
        }
        float distanceBest = float.PositiveInfinity;
        float aimBest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider hit = candidates[i];
            if (hit == null || !hit.enabled || !hit.gameObject.activeInHierarchy) continue;
            GameObject item = ResolveItem(hit);
            if (item == null || item == root.gameObject || item.transform.IsChildOf(root)) continue;
            if (!IsEligible(hit, item)) continue;
            if (CanPickupItem != null && !CanPickupItem(item)) continue;
            if (PickupHandler == null && (!allowStandaloneStorage || localItems.Count >= maxStandaloneItems)) continue;

            Vector3 nearest = hit.ClosestPoint(origin);
            float dist = (nearest - origin).sqrMagnitude;
            if (dist > range * range) continue;
            float aim = 0f;
            if (camera != null)
            {
                Vector3 v = camera.WorldToViewportPoint(hit.bounds.center);
                if (v.z <= 0f) continue;
                aim = Vector2.Distance(new Vector2(v.x, v.y), new Vector2(0.5f, 0.5f));
                if (requireLooking && aim > screenAimTolerance) continue;
            }
            if (requireClearPath && !CanSee(origin, hit.bounds.center, root, item.transform)) continue;
            if (dist < distanceBest - 0.0001f ||
                (Mathf.Abs(dist - distanceBest) <= 0.0001f && aim < aimBest))
            {
                result = item;
                chosen = hit;
                distanceBest = dist;
                aimBest = aim;
            }
        }
    }

    private GameObject ResolveItem(Collider hit)
    {
        // Multiple colliders on one Rigidbody always resolve to ONE item.
        // With a collider-only item, the collider's GameObject is the item root.
        return hit.attachedRigidbody != null ? hit.attachedRigidbody.gameObject : hit.gameObject;
    }

    private bool IsEligible(Collider hit, GameObject item)
    {
        bool hasRigidbody = hit.attachedRigidbody != null;
        bool onPickupLayer = (pickupLayers.value & (1 << item.layer)) != 0;
        switch (eligibility)
        {
            case EligibilityMode.RigidbodyOnly: return hasRigidbody;
            case EligibilityMode.LayerOnly: return onPickupLayer;
            default: return hasRigidbody || onPickupLayer;
        }
    }

    private bool CanSee(Vector3 origin, Vector3 target, Transform root, Transform item)
    {
        Vector3 delta = target - origin;
        float length = delta.magnitude;
        if (length < 0.02f) return true;
        int count = Physics.RaycastNonAlloc(origin, delta / length, lineHits,
            length, sightBlockers, QueryTriggerInteraction.Ignore);
        // When the ray buffer is saturated, fail safely rather than reach through walls.
        if (count == lineHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            Collider other = lineHits[i].collider;
            if (other == null) continue;
            Transform t = other.transform;
            if (t == root || t.IsChildOf(root) || t == item || t.IsChildOf(item)) continue;
            return false;
        }
        return true;
    }

    private void SetFocus(GameObject item, Collider hitbox)
    {
        if (focusedItem == item && focusedCollider == hitbox) return;
        focusedItem = item;
        focusedCollider = hitbox;
        if (item == null) HidePrompt();
        else if (showWorldPrompt)
        {
            BuildPrompt();
            string name = item.name.Replace("(Clone)", "").Trim();
            promptText.text = "[" + KeyLabel() + "] Pick Up " + name;
            float d = Vector3.Distance(SearchOrigin(), hitbox.ClosestPoint(SearchOrigin()));
            labelScale = Mathf.Lerp(nearLabelScale, farLabelScale, Mathf.Clamp01(d / Mathf.Max(0.1f, range)));
        }
        if (FocusChanged != null) FocusChanged(item);
    }

    private void BuildPrompt()
    {
        if (promptObject != null) return;
        promptObject = new GameObject("Current Pickup Label (Runtime)");
        promptObject.transform.SetParent(transform, false);
        MeshRenderer renderer = promptObject.AddComponent<MeshRenderer>();
        promptText = promptObject.AddComponent<TextMesh>();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
        {
            promptText.font = font;
            renderer.sharedMaterial = font.material;
        }
        promptText.anchor = TextAnchor.MiddleCenter;
        promptText.alignment = TextAlignment.Center;
        promptText.fontSize = 60;
        promptText.characterSize = 0.11f;
        promptText.color = labelColor;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        promptObject.SetActive(false);
    }

    private void HidePrompt()
    {
        if (promptObject != null) promptObject.SetActive(false);
    }

    public bool TryPickup()
    {
        if (!isActiveAndEnabled || !InputAllowed) return false;
        // Always verify at the moment of interaction; the displayed focus can be stale.
        GameObject item;
        Collider hit;
        FindClosest(out item, out hit);
        SetFocus(item, hit);
        if (item == null) return false;
        bool accepted = PickupHandler != null
            ? PickupHandler(item)
            : (allowStandaloneStorage && StoreLocally(item));
        if (!accepted) return false;
        SetFocus(null, null);
        nextScanTime = Time.time + 0.12f;
        if (ItemPickedUp != null) ItemPickedUp(item);
        return true;
    }

    private bool StoreLocally(GameObject item)
    {
        if (localItems.Count >= maxStandaloneItems) return false;
        for (int i = 0; i < localItems.Count; i++)
            if (localItems[i].Item == item) return false;
        EnsureStandaloneRoots();
        Stored state = new Stored();
        state.Item = item;
        state.Colliders = item.GetComponentsInChildren<Collider>(true);
        state.ColliderEnabled = new bool[state.Colliders.Length];
        for (int i = 0; i < state.Colliders.Length; i++)
        {
            state.ColliderEnabled[i] = state.Colliders[i].enabled;
            state.Colliders[i].enabled = false;
        }
        state.Rigidbody = item.GetComponent<Rigidbody>();
        if (state.Rigidbody != null)
        {
            state.WasKinematic = state.Rigidbody.isKinematic;
            state.UsedGravity = state.Rigidbody.useGravity;
            state.Rigidbody.isKinematic = true;
            state.Rigidbody.useGravity = false;
        }
        state.OriginalScale = item.transform.localScale;
        item.transform.SetParent(storageRoot, false);
        item.SetActive(false);
        localItems.Add(state);
        return EquipStored(localItems.Count - 1);
    }

    private void EnsureStandaloneRoots()
    {
        Transform root = playerRoot != null ? playerRoot : transform;
        if (storageRoot == null)
        {
            GameObject store = new GameObject("Pickup Storage (Runtime)");
            storageRoot = store.transform;
            storageRoot.SetParent(root, false);
        }
        if (equipMount == null)
        {
            GameObject mount = new GameObject("Pickup Equip Mount (Runtime - Adjust Position)");
            equipMount = mount.transform;
            equipMount.SetParent(root, false);
            equipMount.localPosition = new Vector3(0.35f, 1.25f, 0.55f);
        }
    }

    public bool EquipStored(int index)
    {
        if (PickupHandler != null) return false; // External inventory owns equip behavior.
        if (index < 0 || index >= localItems.Count || localItems[index].Item == null) return false;
        EnsureStandaloneRoots();
        for (int i = 0; i < localItems.Count; i++)
        {
            GameObject item = localItems[i].Item;
            if (item == null) continue;
            bool isSelected = i == index;
            item.transform.SetParent(isSelected ? equipMount : storageRoot, false);
            if (isSelected)
            {
                item.transform.localPosition = Vector3.zero;
                item.transform.localRotation = Quaternion.identity;
                item.transform.localScale = localItems[i].OriginalScale;
            }
            item.SetActive(isSelected);
        }
        equippedLocalIndex = index;
        return true;
    }

    public bool TryDrop()
    {
        if (!InputAllowed) return false;
        if (DropHandler != null) return DropHandler();
        return DropStored();
    }

    public bool DropStored()
    {
        if (equippedLocalIndex < 0 || equippedLocalIndex >= localItems.Count) return false;
        Stored state = localItems[equippedLocalIndex];
        localItems.RemoveAt(equippedLocalIndex);
        equippedLocalIndex = -1;
        if (state.Item != null)
        {
            Transform item = state.Item.transform;
            item.SetParent(null, true);
            Transform root = playerRoot != null ? playerRoot : transform;
            item.position = root.position + root.forward * dropDistance + Vector3.up * 0.75f;
            item.localScale = state.OriginalScale;
            state.Item.SetActive(true);
            for (int i = 0; i < state.Colliders.Length; i++)
                if (state.Colliders[i] != null) state.Colliders[i].enabled = state.ColliderEnabled[i];
            if (state.Rigidbody != null)
            {
                state.Rigidbody.isKinematic = state.WasKinematic;
                state.Rigidbody.useGravity = state.UsedGravity;
            }
        }
        if (localItems.Count > 0) EquipStored(localItems.Count - 1);
        nextScanTime = Time.time + 0.2f;
        return true;
    }

    private string KeyLabel() { return interactKey.ToString(); }

    private bool ReadPickupPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        bool key = kb != null && ((interactKey == InteractionKey.F && kb.fKey.wasPressedThisFrame) ||
            (interactKey == InteractionKey.E && kb.eKey.wasPressedThisFrame) ||
            (interactKey == InteractionKey.R && kb.rKey.wasPressedThisFrame));
        return key || (acceptGamepadWestButton && Gamepad.current != null &&
            Gamepad.current.buttonWest.wasPressedThisFrame);
#elif ENABLE_LEGACY_INPUT_MANAGER
        KeyCode key = interactKey == InteractionKey.E ? KeyCode.E :
            interactKey == InteractionKey.R ? KeyCode.R : KeyCode.F;
        return Input.GetKeyDown(key);
#else
        return false;
#endif
    }

    private bool ReadDropPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        return (kb != null && kb.gKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.dpad.down.wasPressedThisFrame);
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.G);
#else
        return false;
#endif
    }
}
