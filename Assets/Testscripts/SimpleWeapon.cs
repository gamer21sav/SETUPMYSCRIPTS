// Requires the existing IDamageable.cs interface. No inventory/settings dependency.
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent, DefaultExecutionOrder(200)]
public sealed class SimpleWeapon : MonoBehaviour
{
    [Header("Shooting")]
    [Tooltip("Assign the same hand/item holder used by the player's inventory. The weapon counts as equipped only when parented directly to this Transform.")]
    [SerializeField] Transform equippedHolder;
    public Transform muzzle;
    public bool automatic = true;
    [Min(0f)] public float damage = 20f;
    [Min(1f)] public float range = 100f;
    [Min(.1f)] public float roundsPerSecond = 8f;
    public LayerMask hitLayers = ~0;
    [Range(0f, 15f)] public float hipSpread = 1.5f, aimSpread = .15f;
    [Header("Ammo")]
    [Min(1)] public int magazineSize = 30;
    [Min(0)] public int startingReserve = 90;
    [Min(.01f)] public float reloadSeconds = 1.8f;
    public bool autoReload = true;
    [Header("Input: left click / right click / R")]
    public bool readLocalInput = true;
    [Header("Optional effects")]
    public ParticleSystem muzzleFlash;
    public AudioSource audioSource;
    public AudioClip shotSound, reloadSound;
    public LineRenderer tracer;
    [Min(.01f)] public float tracerSeconds = .05f;
    public UnityEvent onShot = new UnityEvent();
    public UnityEvent onReloadStarted = new UnityEvent();
    public UnityEvent onAmmoChanged = new UnityEvent();
    public UnityEvent<bool> onAimChanged = new UnityEvent<bool>();
    public UnityEvent<bool> onReloadChanged = new UnityEvent<bool>();
    public UnityEvent<bool> onEquippedChanged = new UnityEvent<bool>();
    public int Ammo { get; private set; }
    public int Reserve { get; private set; }
    public bool IsReloading { get; private set; }
    public bool IsAiming { get; private set; }
    public System.Action<RaycastHit> Hit;
    Camera viewCamera;
    Transform owner;
    float nextShot, reloadEnd, tracerEnd;
    bool inputReady;
    bool reportedEquipped;
    readonly RaycastHit[] hits = new RaycastHit[32];
    readonly System.Collections.Generic.List<Camera> cameras = new System.Collections.Generic.List<Camera>();
    bool Equipped => owner && viewCamera && viewCamera.isActiveAndEnabled &&
                     equippedHolder && transform.parent == equippedHolder;
    bool LocalAllowed => Application.isFocused && Cursor.lockState == CursorLockMode.Locked;

    void Awake() { Ammo = Mathf.Max(1, magazineSize); Reserve = Mathf.Max(0, startingReserve); }
    void OnEnable() { FindWielder(); inputReady = false; NotifyEquipped(); onAmmoChanged.Invoke(); }
    void OnDisable() { NotifyUnequipped(); CancelReload(); SetAiming(false); if (tracer) tracer.enabled = false; }
    void OnTransformParentChanged() { CancelReload(); SetAiming(false); FindWielder(); inputReady = false; NotifyEquipped(); }
    // Only a gun parented under a player with an active camera accepts local input.
    void FindWielder()
    {
        owner = null; viewCamera = null;
        for (Transform p = transform.parent; p != null; p = p.parent)
        {
            cameras.Clear(); p.GetComponentsInChildren<Camera>(false, cameras);
            foreach (Camera camera in cameras)
                if (camera.isActiveAndEnabled && !camera.transform.IsChildOf(transform))
                { owner = p; viewCamera = camera; return; }
        }
    }
    void LateUpdate()
    {
        if (tracer && Time.time >= tracerEnd) tracer.enabled = false;
        NotifyEquipped();
        if (!Equipped) { CancelReload(); SetAiming(false); inputReady = false; return; }
        if (IsReloading && Time.time >= reloadEnd)
        {
            int amount = Mathf.Min(Mathf.Max(0, magazineSize - Ammo), Reserve);
            Ammo += amount; Reserve -= amount; IsReloading = false; onAmmoChanged.Invoke();
            onReloadChanged.Invoke(false);
        }
        if (!readLocalInput) return;
        if (!LocalAllowed) { SetAiming(false); inputReady = false; return; }
        bool held = false, pressed = false, aiming = false, reload = false;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null) { held = Mouse.current.leftButton.isPressed; pressed = Mouse.current.leftButton.wasPressedThisFrame; aiming = Mouse.current.rightButton.isPressed; }
        reload = Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        held = Input.GetMouseButton(0); pressed = Input.GetMouseButtonDown(0);
        aiming = Input.GetMouseButton(1); reload = Input.GetKeyDown(KeyCode.R);
#endif
        // A click used to capture the cursor must be released before firing.
        if (!inputReady) { if (!held && !aiming) inputReady = true; return; }
        SetAiming(aiming);
        if (reload) TryReload();
        if (automatic ? held : pressed) { if (Ammo == 0 && autoReload) TryReload(); else TryFire(); }
    }
    void NotifyEquipped()
    {
        bool equipped = Equipped;
        if (reportedEquipped == equipped) return;
        reportedEquipped = equipped;
        onEquippedChanged.Invoke(equipped);
    }
    void NotifyUnequipped()
    {
        if (!reportedEquipped) return;
        reportedEquipped = false;
        onEquippedChanged.Invoke(false);
    }
    public void SetAiming(bool value)
    {
        value = value && Equipped && (!readLocalInput || LocalAllowed);
        if (IsAiming == value) return;
        IsAiming = value; onAimChanged.Invoke(value);
    }
    public bool TryReload()
    {
        if (!isActiveAndEnabled || !Equipped || (readLocalInput && !LocalAllowed) || IsReloading || Ammo >= magazineSize || Reserve <= 0) return false;
        IsReloading = true; reloadEnd = Time.time + Mathf.Max(.01f, reloadSeconds);
        if (audioSource && reloadSound) audioSource.PlayOneShot(reloadSound);
        onReloadStarted.Invoke(); onReloadChanged.Invoke(true); return true;
    }
    public void CancelReload()
    {
        if (!IsReloading) return;
        IsReloading = false; onReloadChanged.Invoke(false);
    }
    public void AddAmmo(int amount) { if (amount <= 0) return; Reserve = (int)System.Math.Min(int.MaxValue, (long)Reserve + amount); onAmmoChanged.Invoke(); }
    public bool TryFire()
    {
        if (!isActiveAndEnabled || !Equipped || (readLocalInput && !LocalAllowed) || IsReloading || Ammo <= 0 || Time.time < nextShot) return false;
        nextShot = Time.time + 1f / Mathf.Max(.1f, roundsPerSecond); Ammo--;
        Ray ray = viewCamera.ViewportPointToRay(new Vector3(.5f, .5f));
        Vector2 spread = Random.insideUnitCircle * Mathf.Tan(Mathf.Clamp(IsAiming ? aimSpread : hipSpread, 0f, 15f) * Mathf.Deg2Rad);
        Vector3 direction = (ray.direction + viewCamera.transform.right * spread.x + viewCamera.transform.up * spread.y).normalized;
        float distance = Mathf.Max(1f, range);
        bool found = Cast(ray.origin, direction, distance, out RaycastHit hit);
        Vector3 target = found ? hit.point : ray.origin + direction * distance;
        Vector3 start = muzzle ? muzzle.position : transform.position;
        Vector3 path = target - start;
        // A second ray prevents third-person camera aiming through cover beside the gun.
        if (path.sqrMagnitude > .000001f && Cast(start, path.normalized, path.magnitude, out RaycastHit obstruction))
        { hit = obstruction; target = hit.point; found = true; }
        if (muzzleFlash) muzzleFlash.Play();
        if (audioSource && shotSound) audioSource.PlayOneShot(shotSound);
        if (tracer) { tracer.useWorldSpace = true; tracer.positionCount = 2; tracer.SetPosition(0, start); tracer.SetPosition(1, target); tracer.enabled = true; tracerEnd = Time.time + Mathf.Max(.01f, tracerSeconds); }
        if (found)
        {
            IDamageable receiver = hit.collider.GetComponentInParent<IDamageable>();
            if (receiver != null && receiver.IsAlive) receiver.TakeDamage(Mathf.Max(0f, damage));
            Hit?.Invoke(hit);
        }
        onAmmoChanged.Invoke(); onShot.Invoke(); return true;
    }
    bool Cast(Vector3 origin, Vector3 direction, float distance, out RaycastHit closest)
    {
        int count = Physics.RaycastNonAlloc(origin, direction, hits, distance, hitLayers, QueryTriggerInteraction.Ignore);
        RaycastHit[] results = hits;
        if (count == hits.Length) { results = Physics.RaycastAll(origin, direction, distance, hitLayers, QueryTriggerInteraction.Ignore); count = results.Length; }
        closest = default; float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Transform t = results[i].collider.transform;
            if (t.IsChildOf(transform) || (owner && t.IsChildOf(owner)) || results[i].distance >= nearest) continue;
            nearest = results[i].distance; closest = results[i];
        }
        return nearest < float.PositiveInfinity;
    }
}
