using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Animation-timed melee damage for a held weapon or unarmed attack.
/// Put this component on the same GameObject as the Animator so Animation Events
/// on the attack clip can call its hit-window methods.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class SimpleMeleeWeapon : MonoBehaviour
{
    [Header("Animator")]
    [SerializeField] private Animator animator;
    [SerializeField] private string attackTrigger = "MeleeAttack";
    [SerializeField] private bool readLocalInput;
    [SerializeField] private string legacyAttackButton = "Fire1";

    [Header("Equipped weapon and hit shape")]
    [Tooltip("The hand/equipment transform. Parent the equipped sword, axe, or other weapon here.")]
    [SerializeField] private Transform weaponHolder;
    [Tooltip("Set these to points on the weapon blade. For fists, use the knuckles and a point just ahead of them.")]
    [SerializeField] private Transform hitBase;
    [SerializeField] private Transform hitTip;
    [SerializeField, Min(0.01f)] private float hitRadius = 0.12f;
    [SerializeField] private LayerMask hittableLayers = ~0;

    [Header("Damage")]
    [SerializeField, Min(0f)] private float damage = 20f;
    [Tooltip("Must match the public damage method on the hit target.")]
    [SerializeField] private string damageMethod = "Takedamage";
    [SerializeField, Min(0.05f)] private float attackCooldown = 0.6f;
    [SerializeField, Min(0f)] private float impactDelayWithoutEvents = 0.22f;
    [SerializeField, Min(0.01f)] private float hitWindowWithoutEvents = 0.16f;
    [SerializeField, Min(0.1f)] private float maxAttackTime = 1.5f;

    private readonly HashSet<Transform> hitRoots = new HashSet<Transform>();
    private readonly HashSet<int> animatorTriggers = new HashSet<int>();
    private readonly Collider[] overlapResults = new Collider[32];
    private readonly RaycastHit[] sweepResults = new RaycastHit[32];
    private Transform target;
    private Vector3 previousBase;
    private Vector3 previousTip;
    private float nextAttackTime;
    private float attackStartedAt;
    private bool attacking;
    private bool hitWindowOpen;
    private bool animationEventUsed;
    private bool hasPreviousShape;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        CacheAnimatorTriggers();
        if (weaponHolder == null && animator != null && animator.isHuman)
            weaponHolder = animator.GetBoneTransform(HumanBodyBones.RightHand);
    }

    private void Update()
    {
        if (readLocalInput && AttackPressed()) TryAttack();
    }

    private void LateUpdate()
    {
        if (!attacking) return;
        if (Time.time - attackStartedAt >= maxAttackTime)
        {
            attacking = false;
            hitWindowOpen = false;
            return;
        }

        if (!animationEventUsed)
        {
            float elapsed = Time.time - attackStartedAt;
            float hitWindowEnd = impactDelayWithoutEvents + hitWindowWithoutEvents;
            hitWindowOpen = elapsed >= impactDelayWithoutEvents && elapsed <= hitWindowEnd;
            if (elapsed > hitWindowEnd)
            {
                attacking = false;
                hitWindowOpen = false;
                return;
            }
        }

        if (!hitWindowOpen) return;
        GetHitShape(out Vector3 bladeBase, out Vector3 bladeTip);
        if (hasPreviousShape)
        {
            SweepPoint(previousBase, bladeBase);
            SweepPoint(previousTip, bladeTip);
        }
        ProcessOverlap(bladeBase, bladeTip);
        previousBase = bladeBase;
        previousTip = bladeTip;
        hasPreviousShape = true;
    }

    /// <summary>Call from input code or ParkourChaseAI to begin an attack.</summary>
    public bool TryAttack()
    {
        if (animator == null || !animator.isActiveAndEnabled || attacking ||
            Time.time < nextAttackTime) return false;

        attacking = true;
        hitWindowOpen = false;
        animationEventUsed = false;
        hasPreviousShape = false;
        hitRoots.Clear();
        attackStartedAt = Time.time;
        nextAttackTime = Time.time + Mathf.Max(0.05f, attackCooldown);

        int trigger = Animator.StringToHash(attackTrigger);
        if (animatorTriggers.Contains(trigger))
        {
            animator.ResetTrigger(trigger);
            animator.SetTrigger(trigger);
        }
        return true;
    }

    /// <summary>Assign the victim before calling TryAttack; null allows any target in the hit shape.</summary>
    public void SetTarget(Transform victim) => target = victim;
    public void SetLocalInputEnabled(bool enabled) => readLocalInput = enabled;

    // Add Animation Events at the first active swing frame and the last active frame.
    public void BeginHitWindow()
    {
        if (!attacking) return;
        animationEventUsed = true;
        hitWindowOpen = true;
        hasPreviousShape = false;
    }

    public void EndHitWindow()
    {
        animationEventUsed = true;
        hitWindowOpen = false;
        hasPreviousShape = false;
    }

    // Optional Animation Event at the end of the attack clip.
    public void EndAttackAnimation()
    {
        attacking = false;
        hitWindowOpen = false;
        hasPreviousShape = false;
    }

    private void GetHitShape(out Vector3 bladeBase, out Vector3 bladeTip)
    {
        Transform fallback = weaponHolder != null ? weaponHolder : transform;
        bladeBase = hitBase != null ? hitBase.position : fallback.position;
        bladeTip = hitTip != null ? hitTip.position :
            bladeBase + fallback.forward * 0.8f;
    }

    private void ProcessOverlap(Vector3 bladeBase, Vector3 bladeTip)
    {
        int count = Physics.OverlapCapsuleNonAlloc(bladeBase, bladeTip,
            hitRadius, overlapResults, hittableLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++) ApplyHit(overlapResults[i]);
    }

    private void SweepPoint(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.001f) return;
        int count = Physics.SphereCastNonAlloc(from, hitRadius, delta / distance,
            sweepResults, distance, hittableLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++) ApplyHit(sweepResults[i].collider);
    }

    private void ApplyHit(Collider hitCollider)
    {
        if (hitCollider == null || hitCollider.transform.root == transform.root) return;
        Transform hitRoot = hitCollider.transform.root;
        if (target != null && hitRoot != target.root) return;
        if (!hitRoots.Add(hitRoot)) return;
        if (!string.IsNullOrWhiteSpace(damageMethod))
            hitCollider.SendMessageUpwards(damageMethod, damage, SendMessageOptions.DontRequireReceiver);
    }

    private void CacheAnimatorTriggers()
    {
        animatorTriggers.Clear();
        if (animator == null) return;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
            if (parameter.type == AnimatorControllerParameterType.Trigger)
                animatorTriggers.Add(Animator.StringToHash(parameter.name));
    }

    private bool AttackPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return !string.IsNullOrEmpty(legacyAttackButton) && Input.GetButtonDown(legacyAttackButton);
#else
        return false;
#endif
    }
}