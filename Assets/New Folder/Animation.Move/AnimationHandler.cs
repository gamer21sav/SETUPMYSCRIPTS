using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Visual animation adapter with NO reference to SimpleMove or any project-specific script.
/// Uses only Unity Animator, CharacterController (optional) and observed transform motion.
/// Add to the Player root; Animator on this object or any child is discovered automatically.
/// Movement logic and input remain entirely separate.
///
/// Parameters are optional: only Animator parameters of the correct type are updated.
/// Jumping and Falling are BOOLS, Landing is a TRIGGER, DodgeX / DodgeY are FLOATS.
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

        bool dashing = dashOverrideEnabled
            ? dashOverrideActive
            : detectDodgeFromSpeed && horizontalSpeed >= dodgeDetectionSpeed;
        bool special = dashing || wallRunning || climbing || vaulting;

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
        SetBool(CrouchID, crouching);
        SetBool(SlideID, sliding);
        SetBool(WallRunID, wallRunning);
        SetBool(ClimbID, climbing);
        SetBool(VaultID, vaulting);
        if (dashing)
        {
            SetFloat(DodgeXID, dodgeDirection.x);
            SetFloat(DodgeYID, dodgeDirection.y);
        }

        if (debugStateChanges &&
            (grounded != lastLoggedGrounded || dashing != lastLoggedDashing || falling != lastLoggedFalling))
        {
            Debug.Log($"AnimationHandler: grounded={grounded}, falling={falling}, dash={dashing}, " +
                $"dy={verticalSpeed:F2}, speed={horizontalSpeed:F2}, controllerGrounded=" +
                (characterController != null ? characterController.isGrounded.ToString() : "none"), this);
        }
        lastLoggedGrounded = grounded;
        lastLoggedDashing = dashing;
        lastLoggedFalling = falling;
        previousGrounded = grounded;
        wasDashing = dashing;
        wasJumping = jumping;
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
        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Bool) boolParams.Add(p.nameHash);
            else if (p.type == AnimatorControllerParameterType.Float) floatParams.Add(p.nameHash);
            else if (p.type == AnimatorControllerParameterType.Trigger) triggerParams.Add(p.nameHash);
        }
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
        crouching = isCrouching;
        sliding = isSliding;
        wallRunning = isWallRunning;
        climbing = isClimbing;
        vaulting = isVaulting;
    }
}
