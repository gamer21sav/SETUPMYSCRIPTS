using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Visual animation adapter with NO reference to SimpleMove or any project-specific script.
/// Uses only Unity Animator, CharacterController (optional) and observed transform motion.
/// Add to the Player root; Animator on this object or any child is discovered automatically.
/// Movement logic and input remain entirely separate.
///
/// Parameters are optional: only Animator parameters of the correct type are updated.
/// Jumping and Falling are BOOLS, Landing is a TRIGGER, DodgeX / DodgeY are FLOATS.
/// Optional generic enum-State observation reads exact crouch/slide state when available,
/// otherwise capsule height + speed are used. No custom script is required.
/// Optional APIs let any future motor/AI/network script feed exact action information.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(500)] // Run after a default-order movement motor, before Animator evaluation.
public sealed class AnimationHandler : MonoBehaviour
{
    [Header("Unity references (automatic when left blank)")]
    [SerializeField] private Animator animator;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Transform motionRoot;
    [Header("Optional weapon animation layer")]
    [SerializeField] private string aimingLayerName = "Aiming";

    [Header("Ground detection when no CharacterController exists")]
    [SerializeField] private bool useGroundRaycastFallback = false;
    [SerializeField, Min(0.01f)] private float groundRaycastDistance = 0.23f;
    [SerializeField] private LayerMask groundLayers = ~0;

    [Header("CharacterController ground verification")]
    [Tooltip("Checks for ground directly beneath the capsule, avoiding a stale grounded reading at ledges. Disable if your project uses unusual ground geometry.")]
    [SerializeField] private bool verifyControllerGroundWithRaycast = true;
    [Tooltip("Distance below the capsule feet that still counts as grounded. Keep small to avoid detecting a floor below a ledge.")]
    [SerializeField, Min(0.01f)] private float footProbeDistance = 0.12f;

    [Header("Jump / Fall / Land detection")]
    [SerializeField, Min(0f)] private float jumpUpSpeedThreshold = 0.15f;
    [SerializeField, Min(0f)] private float fallDownSpeedThreshold = 0.15f;
    [Tooltip("Suppresses grounded flickers when landing on bumps or walking down steps.")]
    [SerializeField, Min(0f)] private float minimumAirTimeForLanding = 0.07f;
    [Tooltip("Minimum upward rise to identify an actual jump, including short hops.")]
    [SerializeField, Min(0f)] private float minimumRiseForLanding = 0.06f;
    [Tooltip("Minimum downward travel to count as an actual fall off a ledge.")]
    [SerializeField, Min(0f)] private float minimumDropForLanding = 0.22f;
    [Tooltip("Keep Jumping true briefly so Jump Start can be seen on tiny hops.")]
    [SerializeField, Min(0f)] private float minimumJumpVisualTime = 0.08f;

    [Header("Automatic dodge detection (optional)")]
    [Tooltip("Recognizes fast movement as a dodge without reading an input or movement script.")]
    [SerializeField] private bool detectDodgeFromSpeed = true;
    [Tooltip("Set above sprint speed and below dash speed (SimpleMove defaults: 9.5 and 17).")]
    [SerializeField, Min(0f)] private float dodgeDetectionSpeed = 12.5f;

    [Header("Automatic crouch / slide animation (no custom-script dependency)")]
    [Tooltip("Optionally observe ANY co-located component exposing a public enum property named State with Crouching and Sliding members. No custom class is referenced or required; falls back to the capsule if absent.")]
    [SerializeField] private bool useOptionalEnumState = true;
    [Tooltip("When no compatible public State property is available, infer crouch/slide from CharacterController capsule height and movement speed.")]
    [SerializeField] private bool useCapsuleStanceFallback = true;
    [Tooltip("How much shorter the CharacterController must become before it counts as a crouch. World-unscaled capsule height units.")]
    [SerializeField, Min(0.01f)] private float stanceHeightTolerance = 0.055f;
    [Tooltip("Fallback only: speed when capsule first shortens that suggests a slide (approximate; exact optional state observation is preferred).")]
    [SerializeField, Min(0f)] private float inferredSlideStartSpeed = 6f;
    [Tooltip("Fallback only: below this speed, the inferred slide ends.")]
    [SerializeField, Min(0f)] private float inferredSlideEndSpeed = 3.2f;
    [Tooltip("Fallback only: maximum time the inferred slide stays active.")]
    [SerializeField, Min(0.05f)] private float inferredSlideMaxTime = 0.9f;

    [Header("Animation safety")]
    [Tooltip("CharacterController-driven characters should not be moved by animation root motion.")]
    [SerializeField] private bool disableAnimatorRootMotion = true;
    [Tooltip("Report state changes to the Console (not every frame). Useful when testing ledges.")]
    [SerializeField] private bool debugStateChanges = false;


    private readonly HashSet<int> boolParams = new HashSet<int>();
    private readonly HashSet<int> floatParams = new HashSet<int>();
    private readonly HashSet<int> triggerParams = new HashSet<int>();

    private static readonly int MoveID = Animator.StringToHash("Ismove");
    private static readonly int GroundedID = Animator.StringToHash("Grounded");
    private static readonly int SpeedID = Animator.StringToHash("Speed");
    private static readonly int VerticalID = Animator.StringToHash("VerticalVelocity");
    private static readonly int JumpID = Animator.StringToHash("Jumping");
    private static readonly int FallID = Animator.StringToHash("Falling");
    private static readonly int LandID = Animator.StringToHash("Landing");
    private static readonly int DashID = Animator.StringToHash("Dashing");
    private static readonly int DodgeXID = Animator.StringToHash("DodgeX");
    private static readonly int DodgeYID = Animator.StringToHash("DodgeY");
    private static readonly int CrouchID = Animator.StringToHash("Crouching");
    private static readonly int SlideID = Animator.StringToHash("Sliding");
    private static readonly int WallRunID = Animator.StringToHash("WallRunning");
    private static readonly int ClimbID = Animator.StringToHash("Climbing");
    private static readonly int VaultID = Animator.StringToHash("Vaulting");
    private static readonly int IsAimID = Animator.StringToHash("IsAim");
    private static readonly int IsShootingID = Animator.StringToHash("IsShooting");
    private static readonly int IsReloadingID = Animator.StringToHash("IsReloading");

    private RuntimeAnimatorController cachedController;
    private Vector3 previousPosition;
    private Vector3 airStartPosition;
    private float highestAirY;
    private float lowestAirY;
    private float airTime;
    private float jumpVisualTimer;
    private float horizontalSpeed;
    private float verticalSpeed;
    private bool initialized;
    private bool previousGrounded;
    private bool wasDashing;
    private bool wasJumping;
    private bool sawRealAir;
    private bool lastLoggedGrounded;
    private bool lastLoggedDashing;
    private bool lastLoggedFalling;
    private Vector2 dodgeDirection = Vector2.up;
    private int aimingLayerIndex = -1;
    private bool aiming;
    private bool reloading;
    private bool weaponEquipped;

    // Optional action overrides, invoked by any future movement, AI, replay or network system.
    // No other script is needed for basic locomotion / jump / fall / landing.
    private bool dashOverrideEnabled;
    private bool dashOverrideActive;
    private Vector3 dashOverrideWorldDirection = Vector3.forward;
    private bool crouching;
    private bool sliding;
    private bool wallRunning;
    private bool climbing;
    private bool vaulting;
    private bool manualStanceOverride;
    private bool manualTraversalOverride;
    private MonoBehaviour optionalStateSource;
    private PropertyInfo optionalStateProperty;
    private float standingCapsuleHeight;
    private bool previousCapsuleShortened;
    private bool inferredSliding;
    private float inferredSlideTimer;
    private bool lastLoggedCrouching;
    private bool lastLoggedSliding;
    private readonly RaycastHit[] groundProbeHits = new RaycastHit[16];

    public float ObservedSpeed => horizontalSpeed;
    public float ObservedVerticalSpeed => verticalSpeed;

    private void Awake()
    {
        if (motionRoot == null)
        {
            if (characterController == null)
                characterController = GetComponent<CharacterController>();
            if (characterController == null)
                characterController = GetComponentInParent<CharacterController>();
            if (characterController == null)
                characterController = GetComponentInChildren<CharacterController>(true);
            motionRoot = characterController != null ? characterController.transform : transform;
        }
        if (characterController == null)
            characterController = motionRoot.GetComponent<CharacterController>();

        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (animator == null) animator = GetComponentInParent<Animator>();
        if (animator == null)
        {
            Debug.LogWarning("AnimationHandler: No Animator found. Assign an Animator or place one on a child.", this);
            enabled = false;
            return;
        }
        CacheAnimatorParameters();
        // The standing reference comes from the Unity component itself, not the motor.
        standingCapsuleHeight = characterController != null ? characterController.height : 0f;
        FindOptionalStateSource();
        // A CharacterController-driven motor owns root position; animation must not.
        if (disableAnimatorRootMotion) animator.applyRootMotion = false;
    }

    private void OnEnable()
    {
        initialized = false;
        wasDashing = false;
        wasJumping = false;
        sawRealAir = false;
        airTime = 0f;
        jumpVisualTimer = 0f;
        lastLoggedGrounded = false;
        lastLoggedDashing = false;
        lastLoggedFalling = false;
        lastLoggedCrouching = false;
        lastLoggedSliding = false;
        previousCapsuleShortened = false;
        inferredSliding = false;
        inferredSlideTimer = 0f;
    }

    private void Update()
    {
        if (animator == null || motionRoot == null) return;
        if (animator.runtimeAnimatorController != cachedController) CacheAnimatorParameters();
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 position = motionRoot.position;
        bool grounded = ReadGrounded(position);
        if (!initialized)
        {
            previousPosition = position;
            previousGrounded = grounded;
            airStartPosition = position;
            highestAirY = position.y;
            lowestAirY = position.y;
            initialized = true;
            SetBool(GroundedID, grounded);
            return;
        }
        Vector3 displacement = position - previousPosition;
        previousPosition = position;
        Vector3 horizontalDelta = Vector3.ProjectOnPlane(displacement, Vector3.up);
        horizontalSpeed = horizontalDelta.magnitude / dt;
        verticalSpeed = displacement.y / dt;

        bool stanceCrouching;
        bool stanceSliding;
        ResolveStance(grounded, dt, out stanceCrouching, out stanceSliding);

        bool stateWallRunning = false;
        bool stateClimbing = false;
        bool stateVaulting = false;
        if (!manualTraversalOverride && useOptionalEnumState && optionalStateSource != null &&
            optionalStateSource.isActiveAndEnabled && optionalStateProperty != null)
        {
            object state = optionalStateProperty.GetValue(optionalStateSource, null);
            string stateName = state != null ? state.ToString() : string.Empty;
            stateWallRunning = stateName == "WallRunning";
            stateClimbing = stateName == "WallClimbing";
            stateVaulting = stateName == "Vaulting";
        }
        bool currentWallRunning = manualTraversalOverride ? wallRunning : stateWallRunning;
        bool currentClimbing = manualTraversalOverride ? climbing : stateClimbing;
        bool currentVaulting = manualTraversalOverride ? vaulting : stateVaulting;

        bool dashing = dashOverrideEnabled
            ? dashOverrideActive
            : detectDodgeFromSpeed && horizontalSpeed >= dodgeDetectionSpeed;
        bool special = dashing || currentWallRunning || currentClimbing || currentVaulting;

        // Sample the actual root movement AFTER the motor's Update.
        // No direct references to any motor, input class, or networking library.
        bool rising = !grounded && verticalSpeed > jumpUpSpeedThreshold;
        bool descending = !grounded && verticalSpeed < -fallDownSpeedThreshold;
        bool jumpStarts = rising && !wasJumping;
        if (!grounded && previousGrounded)
        {
            airTime = 0f;
            airStartPosition = position - displacement;
            highestAirY = position.y;
            lowestAirY = position.y;
            sawRealAir = false;
        }
        if (!grounded)
        {
            airTime += dt;
            highestAirY = Mathf.Max(highestAirY, position.y);
            lowestAirY = Mathf.Min(lowestAirY, position.y);
            if (rising) sawRealAir = true;
        }

        if (jumpStarts && !special)
        {
            jumpVisualTimer = minimumJumpVisualTime;
            ResetTrigger(LandID);
        }
        jumpVisualTimer = Mathf.Max(0f, jumpVisualTimer - dt);

        bool jumping = !grounded && !special &&
            (rising || (jumpVisualTimer > 0f && !descending));
        // Even if the apex has nearly zero vertical movement for a frame,
        // an unsupported character is airborne and should leave WalkRun.
        bool falling = !grounded && !special && !jumping &&
            (descending || airTime >= 0.10f);

        if (!previousGrounded && grounded)
        {
            float rise = highestAirY - airStartPosition.y;
            float drop = highestAirY - position.y;
            bool significantJump = sawRealAir && rise >= minimumRiseForLanding;
            bool significantFall = drop >= minimumDropForLanding;
            bool validLanding = airTime >= minimumAirTimeForLanding &&
                                (significantJump || significantFall) && !special;
            if (validLanding) SetTrigger(LandID);
            airTime = 0f;
            sawRealAir = false;
            jumpVisualTimer = 0f;
        }

        // Dashing takes priority over landing even if a Landing trigger was
        // queued on the preceding frame. Animator must ALSO have a
        // Landing -> Dodge transition (Dashing=true, Exit Time OFF).
        if (dashing) ResetTrigger(LandID);

        if (dashing && !wasDashing)
        {
            Vector3 worldDirection = dashOverrideEnabled
                ? dashOverrideWorldDirection : horizontalDelta;
            if (worldDirection.sqrMagnitude < 0.0001f) worldDirection = motionRoot.forward;
            Vector3 local = motionRoot.InverseTransformDirection(worldDirection.normalized);
            dodgeDirection = new Vector2(local.x, local.z);
            if (dodgeDirection.sqrMagnitude < 0.01f) dodgeDirection = Vector2.up;
            else dodgeDirection.Normalize();
        }

        SetBool(GroundedID, grounded);
        SetFloat(SpeedID, horizontalSpeed);
        // Locomotion intent must not be suppressed by an imperfect ground reading.
        // The Animator transitions determine whether WalkRun is active.
        SetBool(MoveID, horizontalSpeed > 0.1f);
        float verticalForBlend = characterController != null && characterController.enabled
            ? characterController.velocity.y : verticalSpeed;
        if (Mathf.Abs(verticalForBlend) < 0.01f) verticalForBlend = 0f;
        SetFloat(VerticalID, verticalForBlend);
        SetBool(JumpID, jumping);
        SetBool(FallID, falling);
        SetBool(DashID, dashing);
        // Slide has visual priority over crouch: never send two competing
        // stance bools at once. Jump and dash also take priority.
        bool showSliding = grounded && !special && stanceSliding;
        bool showCrouching = grounded && !special && stanceCrouching && !showSliding;
        SetBool(CrouchID, showCrouching);
        SetBool(SlideID, showSliding);
        SetBool(WallRunID, currentWallRunning);
        SetBool(ClimbID, currentClimbing);
        SetBool(VaultID, currentVaulting);
        if (dashing)
        {
            SetFloat(DodgeXID, dodgeDirection.x);
            SetFloat(DodgeYID, dodgeDirection.y);
        }

        if (debugStateChanges &&
            (grounded != lastLoggedGrounded || dashing != lastLoggedDashing || falling != lastLoggedFalling ||
             showCrouching != lastLoggedCrouching || showSliding != lastLoggedSliding))
        {
            Debug.Log($"AnimationHandler: grounded={grounded}, falling={falling}, dash={dashing}, " +
                $"crouch={showCrouching}, slide={showSliding}, dy={verticalSpeed:F2}, speed={horizontalSpeed:F2}, controllerGrounded=" +
                (characterController != null ? characterController.isGrounded.ToString() : "none"), this);
        }
        lastLoggedGrounded = grounded;
        lastLoggedDashing = dashing;
        lastLoggedFalling = falling;
        lastLoggedCrouching = showCrouching;
        lastLoggedSliding = showSliding;
        previousGrounded = grounded;
        wasDashing = dashing;
        wasJumping = jumping;
    }

    // Optional generic state observation: no reference to SimpleMove or any
    // custom game type. A component with any matching enum State can provide
    // exact crouch/slide flags. Without one, detection uses only Unity APIs.
    private void FindOptionalStateSource()
    {
        optionalStateSource = null;
        optionalStateProperty = null;
        if (!useOptionalEnumState || motionRoot == null) return;

        MonoBehaviour[] components = motionRoot.GetComponents<MonoBehaviour>();
        foreach (MonoBehaviour component in components)
        {
            if (component == null || component == this) continue;
            PropertyInfo property = component.GetType().GetProperty("State", BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanRead || property.GetIndexParameters().Length != 0 ||
                !property.PropertyType.IsEnum) continue;
            string[] names = System.Enum.GetNames(property.PropertyType);
            bool hasCrouching = false;
            bool hasSliding = false;
            foreach (string name in names)
            {
                if (name == "Crouching") hasCrouching = true;
                if (name == "Sliding") hasSliding = true;
            }
            if (!hasCrouching || !hasSliding) continue;
            optionalStateSource = component;
            optionalStateProperty = property;
            break;
        }
    }

    private void ResolveStance(bool grounded, float dt, out bool isCrouching, out bool isSliding)
    {
        isCrouching = false;
        isSliding = false;

        // Existing, backwards-compatible method has precedence when used.
        if (manualStanceOverride)
        {
            isSliding = sliding;
            isCrouching = crouching && !sliding;
            return;
        }

        // Uses only a generic public property, not a compiled dependency.
        if (useOptionalEnumState && optionalStateSource != null &&
            optionalStateSource.isActiveAndEnabled && optionalStateProperty != null)
        {
            object state = optionalStateProperty.GetValue(optionalStateSource, null);
            string name = state != null ? state.ToString() : string.Empty;
            isSliding = name == "Sliding";
            isCrouching = name == "Crouching";
            return;
        }

        if (!useCapsuleStanceFallback || characterController == null ||
            !characterController.enabled || standingCapsuleHeight <= 0f) return;

        // Approximation for fully standalone use with any motor that changes
        // CharacterController.height to crouch. Fast crouch on first entry is
        // interpreted as slide; later high crouch speed does not re-trigger it.
        bool shortened = characterController.height < standingCapsuleHeight - stanceHeightTolerance;
        if (!shortened || !grounded)
        {
            inferredSliding = false;
            inferredSlideTimer = 0f;
        }
        else
        {
            if (!previousCapsuleShortened && horizontalSpeed >= inferredSlideStartSpeed)
            {
                inferredSliding = true;
                inferredSlideTimer = 0f;
            }
            if (inferredSliding)
            {
                inferredSlideTimer += dt;
                if (horizontalSpeed <= inferredSlideEndSpeed ||
                    inferredSlideTimer >= inferredSlideMaxTime)
                    inferredSliding = false;
            }
        }
        previousCapsuleShortened = shortened;
        isSliding = grounded && shortened && inferredSliding;
        isCrouching = grounded && shortened && !isSliding;
    }

    private bool ReadGrounded(Vector3 rootPosition)
    {
        if (characterController != null && characterController.enabled)
        {
            if (!characterController.isGrounded) return false;
            if (!verifyControllerGroundWithRaycast) return true;

            // Probe immediately below the controller capsule's feet. The small
            // range rejects a platform far below while leaving room for skin width.
            Transform t = characterController.transform;
            Vector3 center = t.TransformPoint(characterController.center);
            float height = characterController.height * Mathf.Abs(t.lossyScale.y);
            float bottomY = center.y - height * 0.5f;
            float originY = bottomY + 0.10f;
            Vector3 origin = new Vector3(center.x, originY, center.z);
            // Filter out colliders belonging to this character (the user's
            // groundLayers may otherwise include the Player layer).
            int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down,
                groundProbeHits, 0.10f + footProbeDistance, groundLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = groundProbeHits[i];
                if (hit.collider == null) continue;
                if (hit.collider.transform.root == t.root) continue;
                if (hit.normal.y > 0.45f) return true;
            }
            return false;
        }
        if (!useGroundRaycastFallback) return false;
        return Physics.Raycast(rootPosition + Vector3.up * 0.05f, Vector3.down,
            groundRaycastDistance, groundLayers, QueryTriggerInteraction.Ignore);
    }

    private void CacheAnimatorParameters()
    {
        boolParams.Clear();
        floatParams.Clear();
        triggerParams.Clear();
        cachedController = animator.runtimeAnimatorController;
        aimingLayerIndex = string.IsNullOrEmpty(aimingLayerName) ? -1 : animator.GetLayerIndex(aimingLayerName);
        if (aimingLayerIndex >= 0) animator.SetLayerWeight(aimingLayerIndex, weaponEquipped ? 1f : 0f);
        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Bool) boolParams.Add(p.nameHash);
            else if (p.type == AnimatorControllerParameterType.Float) floatParams.Add(p.nameHash);
            else if (p.type == AnimatorControllerParameterType.Trigger) triggerParams.Add(p.nameHash);
        }
        SetBool(IsAimID, aiming);
        ResetTrigger(IsShootingID);
        SetBool(IsReloadingID, reloading);
    }

    private void SetBool(int hash, bool value)
    {
        if (boolParams.Contains(hash)) animator.SetBool(hash, value);
    }

    private void SetFloat(int hash, float value)
    {
        if (floatParams.Contains(hash)) animator.SetFloat(hash, value);
    }

    private void SetTrigger(int hash)
    {
        if (triggerParams.Contains(hash)) animator.SetTrigger(hash);
    }

    private void ResetTrigger(int hash)
    {
        if (triggerParams.Contains(hash)) animator.ResetTrigger(hash);
    }

    /// <summary>Connect SimpleWeapon.onAimChanged (dynamic bool) here.</summary>
    public void SetAiming(bool active)
    {
        aiming = active && weaponEquipped;
        SetBool(IsAimID, aiming);
    }

    /// <summary>Connect SimpleWeapon.onShot here; briefly raises IsShooting.</summary>
    public void PlayShooting()
    {
        if (!weaponEquipped) return;
        SetTrigger(IsShootingID);
    }

    /// <summary>Optional direct bool setter for other animation event sources.</summary>
    public void SetShooting(bool active)
    {
        if (active && weaponEquipped) SetTrigger(IsShootingID);
        else ResetTrigger(IsShootingID);
    }

    /// <summary>Connect SimpleWeapon.onReloadChanged (dynamic bool) here.</summary>
    public void SetReloading(bool active)
    {
        reloading = active && weaponEquipped;
        SetBool(IsReloadingID, reloading);
    }

    /// <summary>Connect SimpleWeapon.onEquippedChanged (dynamic bool) here.</summary>
    public void SetWeaponEquipped(bool equipped)
    {
        weaponEquipped = equipped;
        if (!weaponEquipped)
        {
            aiming = false;
            reloading = false;
            ResetTrigger(IsShootingID);
        }
        if (aimingLayerIndex >= 0) animator.SetLayerWeight(aimingLayerIndex, weaponEquipped ? 1f : 0f);
        SetBool(IsAimID, aiming);
        SetBool(IsReloadingID, reloading);
    }

    /// <summary>Optional call from ANY motor; not required to compile or to animate jumping.</summary>
    public void SetDashing(bool active, Vector3 worldDirection)
    {
        dashOverrideEnabled = true;
        dashOverrideActive = active;
        if (worldDirection.sqrMagnitude > 0.0001f)
            dashOverrideWorldDirection = worldDirection.normalized;
    }

    /// <summary>Return to automatic speed-based dodge recognition.</summary>
    public void ClearDashOverride()
    {
        dashOverrideEnabled = false;
        dashOverrideActive = false;
    }

    /// <summary>Optional flags for controllers that expose actions; no compile-time dependency.</summary>
    public void SetTraversalFlags(bool isCrouching, bool isSliding,
                                  bool isWallRunning, bool isClimbing, bool isVaulting)
    {
        // Caller-supplied stance values take precedence until cleared.
        manualStanceOverride = true;
        manualTraversalOverride = true;
        crouching = isCrouching;
        sliding = isSliding;
        wallRunning = isWallRunning;
        climbing = isClimbing;
        vaulting = isVaulting;
    }

    /// <summary>Resume automatic stance and traversal-state detection after SetTraversalFlags.</summary>
    public void ClearTraversalStanceOverride()
    {
        manualStanceOverride = false;
        manualTraversalOverride = false;
    }
}
