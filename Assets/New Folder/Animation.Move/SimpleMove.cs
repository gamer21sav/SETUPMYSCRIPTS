using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Reusable single-character kinematic motor: walk, gradual sprint momentum,
/// jump, coyote time, double/air dash, slide, crouch, wall-run, wall-climb,
/// wall-jump, and short vaults.
///
/// Works without GameInput, MoveInput, NetworkRole, SettingsUI, or other project scripts.
/// Uses legacy keyboard fallback when available, or IMoveCommandSource adapters.
/// All regular motion uses a CharacterController; network prediction is not included.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class SimpleMove : MonoBehaviour
{
    public enum MotionState
    {
        Grounded, Airborne, Crouching, Sliding, Dashing, WallRunning,
        WallClimbing, Vaulting
    }

    [Header("Optional Auto-Discovered References")]
    [SerializeField] private CharacterController characterController;
    [SerializeField] private MonoBehaviour commandSource;
    [SerializeField] private Transform cameraTransform;
    [Tooltip("Locally simulated until an offline switcher or network adapter disables it.")]
    [SerializeField] private bool localPlayer = true;

    [Header("Ground Motion")]
    [SerializeField, Min(0f)] private float walkSpeed = 5.5f;
    [SerializeField, Min(0f)] private float sprintSpeed = 9.5f;
    [SerializeField, Min(0.01f)] private float sprintBuildTime = 1.2f;
    [SerializeField, Min(0f)] private float groundAcceleration = 23f;
    [SerializeField, Min(0f)] private float groundBraking = 17f;
    [SerializeField, Range(0f, 1f)] private float airControl = .35f;
    [SerializeField, Min(0f)] private float airBraking = 1.3f;
    [SerializeField, Min(0f)] private float turnSpeed = 720f;
    [SerializeField] private bool orientBodyToMove = true;

    [Header("Jump / Fall")]
    [SerializeField, Min(0.01f)] private float jumpHeight = 1.65f;
    [SerializeField] private float gravity = -25f;
    [SerializeField, Min(0f)] private float terminalFallSpeed = 36f;
    [SerializeField, Min(0f)] private float coyoteTime = .13f;
    [SerializeField, Min(0f)] private float jumpBufferTime = .14f;
    [SerializeField, Range(0.1f, 1f)] private float jumpCutMultiplier = .5f;

    [Header("Dash / Air Dash")]
    [SerializeField] private bool allowDashing = true;
    [SerializeField, Min(0f)] private float dashSpeed = 17f;
    [SerializeField, Min(0.01f)] private float dashDuration = .19f;
    [SerializeField, Min(0f)] private float dashCooldown = .8f;
    [SerializeField, Min(0)] private int airDashesPerJump = 1;
    [SerializeField, Range(0f, 1f)] private float dashExitMomentum = .85f;
    [Tooltip("How much gravity continues during an AIR dodge. 1 = normal gravity; 0 = freeze in air.")]
    [SerializeField, Range(0.1f, 1f)] private float airDashGravityScale = 0.65f;

    [Header("Crouch / Slide")]
    [SerializeField] private bool allowCrouching = true;
    [SerializeField, Min(.3f)] private float crouchHeight = 1.15f;
    [SerializeField, Range(.1f, 1f)] private float crouchSpeedFactor = .5f;
    [SerializeField, Min(0f)] private float stanceTransitionSpeed = 5f;
    [SerializeField] private bool allowSliding = true;
    [SerializeField, Min(0f)] private float slideMinSpeed = 6.3f;
    [SerializeField, Min(0f)] private float slideEntryBoost = 2f;
    [SerializeField, Min(.05f)] private float slideDuration = .9f;
    [SerializeField, Min(0f)] private float slideFriction = 5f;
    [SerializeField, Min(0f)] private float slideSteer = 130f;

    [Header("Wall Run / Jump")]
    [SerializeField] private bool allowWallRun = true;
    [SerializeField, Min(0f)] private float wallProbeDistance = .85f;
    [SerializeField] private LayerMask worldMask = ~0;
    [SerializeField, Min(0f)] private float wallRunMinSpeed = 4f;
    [SerializeField, Min(0f)] private float wallRunSpeed = 9f;
    [SerializeField, Min(.1f)] private float wallRunMaxTime = 1.5f;
    [SerializeField, Range(0f, 1f)] private float wallRunGravityFactor = .18f;
    [SerializeField, Min(0f)] private float wallJumpUpSpeed = 8f;
    [SerializeField, Min(0f)] private float wallJumpAwaySpeed = 6.5f;
    [SerializeField, Min(0f)] private float wallReattachDelay = .28f;

    [Header("Wall Climb")]
    [SerializeField] private bool allowWallClimb = true;
    [SerializeField, Min(0f)] private float wallClimbSpeed = 3.4f;
    [SerializeField, Min(.1f)] private float wallClimbMaxTime = 1.15f;
    [SerializeField, Min(0f)] private float wallClimbProbeDistance = .8f;

    [Header("Vault (short obstacles; collider-based)")]
    [SerializeField] private bool allowVaulting = true;
    [SerializeField, Min(.1f)] private float vaultMaxHeight = 1.2f;
    [SerializeField, Min(.1f)] private float vaultMinHeight = .35f;
    [SerializeField, Min(0f)] private float vaultProbeDistance = .95f;
    [SerializeField, Min(.05f)] private float vaultDuration = .34f;
    [SerializeField, Min(0f)] private float vaultBeyondDistance = 1.15f;
    [SerializeField, Min(0f)] private float vaultArcHeight = .55f;

    [Header("Offline / Networking Preparation")]
    [Tooltip("Only for BotSwitcher offline testing. Disable for network-replicated proxies.")]
    [SerializeField] private bool simulateGravityWhenNotLocal = true;
    [Tooltip("Enable on proxies when networking controls their positions.")]
    [SerializeField] private bool replicatedProxy;
    [Tooltip("Allows an AI/network adapter to SubmitExternalCommand without keyboard input.")]
    [SerializeField] private bool externalSimulation;

    private IMoveCommandSource commands;
    private IMoveCameraRig cameraRig;

    private Vector3 planarVelocity;
    private float verticalVelocity;
    private float sprintProgress;
    private float jumpBufferTimer;
    private float coyoteTimer;
    private float dashTimer;
    private float dashAvailableTime;
    private float slideTimer;
    private float wallTimer;
    private float wallDetachUntil;
    private int airDashesRemaining;
    private bool jumpCutConsumed;
    private bool desiredCrouch;
    private bool standBlocked;
    private bool gameplayInputAllowed = true;
    private bool orientToView;
    private Vector3 wallNormal;
    private Vector3 wallTravel;
    private Vector3 dashDirection;
    private Vector3 vaultStart;
    private Vector3 vaultEnd;
    private float vaultElapsed;
    private float standingHeight;
    private Vector3 standingCenter;
    private readonly Collider[] clearanceResults = new Collider[16];
    private MoveCommand externalCommand;
    private bool hasExternalCommand;

    public MotionState State { get; private set; } = MotionState.Airborne;
    public Vector3 Velocity => planarVelocity + Vector3.up * verticalVelocity;
    public float Speed => planarVelocity.magnitude;
    public float SprintMomentum => sprintProgress;
    public bool Grounded => characterController != null && characterController.isGrounded;
    public bool IsLocallyControlled => localPlayer;
    public bool IsSimulating => !replicatedProxy;
    public bool GameplayInputAllowed => gameplayInputAllowed;
    public bool SimulateGravityWhenNotLocal { get => simulateGravityWhenNotLocal; set => simulateGravityWhenNotLocal = value; }
    public CharacterController Controller => characterController;
    public Transform CameraTransform => cameraTransform;
    public MoveCommand LastCommand { get; private set; }
    public Vector3 DashDirection => dashDirection;
    private void Awake()
    {
        if (characterController == null) characterController = GetComponent<CharacterController>();
        foreach (MonoBehaviour component in GetComponents<MonoBehaviour>())
            if (component is IMoveCameraRig cameraAdapter) { cameraRig = cameraAdapter; break; }

        if (commandSource != null) commands = commandSource as IMoveCommandSource;
        if (commands == null)
        {
            MonoBehaviour[] components = GetComponents<MonoBehaviour>();
            foreach (MonoBehaviour candidate in components)
            {
                if (candidate is IMoveCommandSource source)
                {
                    commands = source;
                    commandSource = candidate;
                    break;
                }
            }
        }
        if (cameraTransform == null)
        {
            Camera ownCamera = GetComponentInChildren<Camera>(true);
            if (ownCamera != null) cameraTransform = ownCamera.transform;
        }
        if (cameraRig != null && cameraTransform != null)
            cameraRig.SetCamera(cameraTransform);

        standingHeight = characterController.height;
        standingCenter = characterController.center;
        crouchHeight = Mathf.Clamp(crouchHeight, characterController.radius * 2.05f, standingHeight);
        airDashesRemaining = airDashesPerJump;
        ApplyLocalState();
    }

    private void OnEnable() => ApplyLocalState();

    private void Update()
    {
        if (characterController == null || !characterController.enabled || replicatedProxy) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        bool inputAuthority = externalSimulation || IsLocallyControlled;
        MoveCommand command = MoveCommand.Empty;
        if (externalSimulation)
        {
            if (hasExternalCommand) command = externalCommand;
            hasExternalCommand = false;
        }
        else if (inputAuthority && gameplayInputAllowed)
        {
            // The fallback must run when no adapter exists. Otherwise a fresh
            // project silently gets zero movement commands.
            command = commands != null ? commands.SampleCommand() : SampleBuiltInKeyboard();
        }
        LastCommand = command;

        // Offline players still fall and retain vertical collision responses.
        // Network proxies instead set replicatedProxy=true and are not simulated here.
        if (!inputAuthority && !simulateGravityWhenNotLocal) return;
        bool passive = !inputAuthority || (!externalSimulation && !gameplayInputAllowed);
        Simulate(command, dt, passive);
    }

    // Keyboard fallback lives in this file. It is compiled out when the legacy
    // Input Manager is disabled, so the motor still compiles in Input System-only
    // projects. Attach PortableNewInputSource in those projects.
    private MoveCommand SampleBuiltInKeyboard()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        MoveCommand result = MoveCommand.Empty;
        result.Move = Vector2.ClampMagnitude(new Vector2(
            (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
            (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f)), 1f);
        result.Look = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        result.JumpPressed = Input.GetKeyDown(KeyCode.Space);
        result.JumpHeld = Input.GetKey(KeyCode.Space);
        result.SprintHeld = Input.GetKey(KeyCode.LeftShift);
        result.CrouchHeld = Input.GetKey(KeyCode.LeftControl);
        result.CrouchPressed = Input.GetKeyDown(KeyCode.LeftControl);
        result.SlidePressed = Input.GetKeyDown(KeyCode.C);
        result.DashPressed = Input.GetKeyDown(KeyCode.Q);
        result.ClimbHeld = Input.GetKey(KeyCode.Space);
        result.ToggleViewPressed = Input.GetKeyDown(KeyCode.V);
        return result;
#else
        return MoveCommand.Empty;
#endif
    }

    private void Simulate(MoveCommand cmd, float dt, bool passive)
    {
        bool grounded = characterController.isGrounded;
        if (grounded)
        {
            coyoteTimer = coyoteTime;
            airDashesRemaining = airDashesPerJump;
            jumpCutConsumed = false;
            if (verticalVelocity < 0f) verticalVelocity = -2f;
        }
        else coyoteTimer -= dt;

        if (cmd.JumpPressed) jumpBufferTimer = jumpBufferTime;
        else jumpBufferTimer = Mathf.Max(0f, jumpBufferTimer - dt);

        Vector3 wish = CalculateWishDirection(cmd.Move);
        UpdateSprintMomentum(cmd, grounded, dt);

        if (State == MotionState.Vaulting)
        {
            SimulateVault(dt);
            return;
        }

        if (State == MotionState.Dashing)
        {
            SimulateDash(dt, grounded);
        }
        else if (!passive)
        {
            if (TryStartVault(cmd, wish, grounded))
            {
                SimulateVault(dt);
                return;
            }

            if (TryStartDash(cmd, wish, grounded))
            {
                SimulateDash(dt, grounded);
            }
            else
            {
                HandleWallAbilities(cmd, wish, grounded, dt);
                HandleJump(cmd, grounded);
                HandleGroundAndAir(cmd, wish, grounded, dt);
            }
        }
        else
        {
            // Unselected offline players should not lock in a wall run or dash.
            if (State == MotionState.WallRunning || State == MotionState.WallClimbing ||
                State == MotionState.Sliding)
                State = MotionState.Airborne;
            if (State != MotionState.Dashing)
                planarVelocity = Vector3.MoveTowards(planarVelocity, Vector3.zero,
                    (grounded ? groundBraking : airBraking) * dt);
        }

        if (State != MotionState.WallClimbing)
        {
            float factor = State == MotionState.WallRunning ? wallRunGravityFactor : 1f;
            if (State == MotionState.Dashing && !grounded) factor = airDashGravityScale;
            // During grounded dash, retain the small downward stick-to-ground
            // velocity; otherwise the dash would dig into the floor each frame.
            if (State != MotionState.Dashing || !grounded)
                verticalVelocity = Mathf.Max(-terminalFallSpeed, verticalVelocity + gravity * factor * dt);
        }

        CollisionFlags flags = characterController.Move((planarVelocity + Vector3.up * verticalVelocity) * dt);
        PostMoveCollision(flags, grounded);
        UpdateStance(cmd, dt);
        RotateBody(wish, dt);
    }

    private Vector3 CalculateWishDirection(Vector2 move)
    {
        Transform view = cameraTransform;
        Vector3 forward = view != null ? Vector3.ProjectOnPlane(view.forward, Vector3.up) : transform.forward;
        Vector3 right = view != null ? Vector3.ProjectOnPlane(view.right, Vector3.up) : transform.right;
        if (forward.sqrMagnitude < .0001f) forward = transform.forward;
        if (right.sqrMagnitude < .0001f) right = transform.right;
        return Vector3.ClampMagnitude(right.normalized * move.x + forward.normalized * move.y, 1f);
    }

    private void UpdateSprintMomentum(MoveCommand cmd, bool grounded, float dt)
    {
        float goal = grounded && cmd.SprintHeld && cmd.Move.y > .1f && !desiredCrouch ? 1f : 0f;
        float rate = goal > sprintProgress ? 1f / Mathf.Max(.01f, sprintBuildTime) : 1.8f;
        sprintProgress = Mathf.MoveTowards(sprintProgress, goal, rate * dt);
    }

    private bool TryStartDash(MoveCommand cmd, Vector3 wish, bool grounded)
    {
        if (!allowDashing || !cmd.DashPressed || Time.time < dashAvailableTime) return false;
        if (!grounded && airDashesRemaining <= 0) return false;
        if (!grounded) airDashesRemaining--;
        dashDirection = wish.sqrMagnitude > .01f ? wish.normalized :
            (planarVelocity.sqrMagnitude > .01f ? planarVelocity.normalized : transform.forward);
        dashDirection.y = 0f;
        dashDirection.Normalize();
        State = MotionState.Dashing;
        dashTimer = dashDuration;
        dashAvailableTime = Time.time + dashCooldown;
        desiredCrouch = false;
        // Preserve vertical momentum for airborne dashes. Zeroing it caused
        // the character to hover on ledges and during falling dodges.
        if (grounded && verticalVelocity < 0f) verticalVelocity = -2f;
        return true;
    }

    private void SimulateDash(float dt, bool grounded)
    {
        dashTimer -= dt;
        planarVelocity = dashDirection * dashSpeed;
        // Do not freeze verticalVelocity. Gravity runs in Simulate while airborne.
        if (dashTimer <= 0f)
        {
            planarVelocity *= dashExitMomentum;
            State = grounded ? MotionState.Grounded : MotionState.Airborne;
        }
    }

    private void HandleWallAbilities(MoveCommand cmd, Vector3 wish, bool grounded, float dt)
    {
        if (grounded)
        {
            wallTimer = 0f;
            if (State == MotionState.WallRunning || State == MotionState.WallClimbing)
                State = MotionState.Grounded;
            return;
        }
        if (Time.time < wallDetachUntil)
        {
            if (State == MotionState.WallRunning || State == MotionState.WallClimbing)
                State = MotionState.Airborne;
            return;
        }

        bool haveSideWall = TrySideWall(out RaycastHit sideHit);
        bool haveFrontWall = TryFrontWall(wish, out RaycastHit frontHit);

        if (State == MotionState.WallRunning)
        {
            wallTimer += dt;
            if (!haveSideWall || wallTimer >= wallRunMaxTime ||
                (!cmd.SprintHeld && !cmd.JumpHeld) || cmd.Move.y < .1f)
            {
                ExitWall();
                return;
            }
            wallNormal = sideHit.normal;
            Vector3 tangent = Vector3.Cross(Vector3.up, wallNormal).normalized;
            if (Vector3.Dot(tangent, wish.sqrMagnitude > .01f ? wish : transform.forward) < 0)
                tangent = -tangent;
            wallTravel = tangent;
            planarVelocity = Vector3.MoveTowards(planarVelocity,
                tangent * wallRunSpeed, groundAcceleration * dt);
            verticalVelocity = Mathf.Max(verticalVelocity, -1.4f);
            return;
        }

        if (State == MotionState.WallClimbing)
        {
            wallTimer += dt;
            if (!haveFrontWall || !cmd.ClimbHeld || cmd.Move.y <= .1f || wallTimer >= wallClimbMaxTime)
            {
                ExitWall();
                return;
            }
            wallNormal = frontHit.normal;
            planarVelocity = Vector3.zero;
            verticalVelocity = wallClimbSpeed;
            return;
        }

        if (allowWallClimb && haveFrontWall && cmd.ClimbHeld && cmd.Move.y > .2f &&
            verticalVelocity <= .5f && cmd.Move.sqrMagnitude > .01f)
        {
            State = MotionState.WallClimbing;
            wallTimer = 0f;
            wallNormal = frontHit.normal;
            planarVelocity = Vector3.zero;
            verticalVelocity = wallClimbSpeed;
        }
        else if (allowWallRun && haveSideWall && cmd.SprintHeld && cmd.Move.y > .25f &&
            planarVelocity.magnitude >= wallRunMinSpeed)
        {
            State = MotionState.WallRunning;
            wallTimer = 0f;
            wallNormal = sideHit.normal;
            Vector3 tangent = Vector3.Cross(Vector3.up, wallNormal).normalized;
            if (Vector3.Dot(tangent, wish) < 0) tangent = -tangent;
            wallTravel = tangent;
            planarVelocity = Vector3.MoveTowards(planarVelocity, tangent * wallRunSpeed,
                groundAcceleration * dt);
        }
    }

    private void ExitWall()
    {
        if (State == MotionState.WallRunning || State == MotionState.WallClimbing)
        {
            State = MotionState.Airborne;
            wallDetachUntil = Time.time + wallReattachDelay;
        }
    }

    private bool TrySideWall(out RaycastHit hit)
    {
        Vector3 origin = transform.position + Vector3.up * characterController.height * .5f;
        if (CastWall(origin, transform.right, wallProbeDistance, out hit)) return true;
        return CastWall(origin, -transform.right, wallProbeDistance, out hit);
    }

    private bool TryFrontWall(Vector3 wish, out RaycastHit hit)
    {
        Vector3 forward = wish.sqrMagnitude > .01f ? wish.normalized : transform.forward;
        Vector3 origin = transform.position + Vector3.up * characterController.height * .55f;
        return CastWall(origin, forward, wallClimbProbeDistance, out hit);
    }

    private bool CastWall(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
    {
        if (Physics.Raycast(origin, direction, out hit, distance, worldMask,
            QueryTriggerInteraction.Ignore))
        {
            // Ignore walkable slopes and ceilings; identify near-vertical walls.
            return Mathf.Abs(hit.normal.y) < .23f && hit.collider.transform.root != transform.root;
        }
        return false;
    }

    private void HandleJump(MoveCommand cmd, bool grounded)
    {
        if (jumpBufferTimer <= 0f) return;

        if (State == MotionState.WallRunning || State == MotionState.WallClimbing)
        {
            verticalVelocity = wallJumpUpSpeed;
            planarVelocity += wallNormal * wallJumpAwaySpeed;
            jumpBufferTimer = 0f;
            jumpCutConsumed = false;
            ExitWall();
            return;
        }

        if (coyoteTimer > 0f && State != MotionState.Dashing)
        {
            verticalVelocity = Mathf.Sqrt(Mathf.Max(.001f, jumpHeight * -2f * gravity));
            coyoteTimer = 0f;
            jumpBufferTimer = 0f;
            jumpCutConsumed = false;
            State = MotionState.Airborne;
        }
    }

    private void HandleGroundAndAir(MoveCommand cmd, Vector3 wish, bool grounded, float dt)
    {
        if (State == MotionState.WallRunning || State == MotionState.WallClimbing) return;

        if (!cmd.JumpHeld && verticalVelocity > 0f && !jumpCutConsumed && coyoteTimer <= 0f)
        {
            verticalVelocity *= jumpCutMultiplier;
            jumpCutConsumed = true;
        }

        if (State == MotionState.Sliding)
        {
            slideTimer -= dt;
            Vector3 direction = planarVelocity.sqrMagnitude > .001f ? planarVelocity.normalized : transform.forward;
            if (wish.sqrMagnitude > .01f)
                direction = Vector3.RotateTowards(direction, wish, slideSteer * dt * Mathf.Deg2Rad, 0f);
            planarVelocity = Vector3.MoveTowards(direction * planarVelocity.magnitude,
                Vector3.zero, slideFriction * dt);
            if (slideTimer <= 0f || planarVelocity.magnitude < walkSpeed * .6f || !grounded)
                State = grounded ? MotionState.Crouching : MotionState.Airborne;
            return;
        }

        desiredCrouch = allowCrouching && cmd.CrouchHeld;
        bool slidingWanted = allowSliding && grounded &&
            (cmd.SlidePressed || (cmd.CrouchPressed && cmd.SprintHeld));
        if (slidingWanted && planarVelocity.magnitude >= slideMinSpeed)
        {
            State = MotionState.Sliding;
            slideTimer = slideDuration;
            planarVelocity += planarVelocity.normalized * slideEntryBoost;
            desiredCrouch = true;
            return;
        }

        if (grounded && State != MotionState.Airborne)
            State = desiredCrouch ? MotionState.Crouching : MotionState.Grounded;
        else if (!grounded) State = MotionState.Airborne;

        float targetSpeed = Mathf.Lerp(walkSpeed, sprintSpeed, sprintProgress);
        if (desiredCrouch && grounded) targetSpeed = walkSpeed * crouchSpeedFactor;
        Vector3 targetVelocity = wish * targetSpeed;
        if (grounded)
        {
            planarVelocity = Vector3.MoveTowards(planarVelocity, targetVelocity,
                (wish.sqrMagnitude > .01f ? groundAcceleration : groundBraking) * dt);
        }
        else
        {
            float rate = wish.sqrMagnitude > .01f ? groundAcceleration * airControl : airBraking;
            planarVelocity = Vector3.MoveTowards(planarVelocity, targetVelocity, rate * dt);
        }
    }

    private bool TryStartVault(MoveCommand cmd, Vector3 wish, bool grounded)
    {
        if (!allowVaulting || !grounded || !cmd.JumpPressed || cmd.Move.y < .2f ||
            wish.sqrMagnitude < .01f || characterController.height < vaultMaxHeight) return false;

        Vector3 start = transform.position;
        Vector3 forward = wish.normalized;
        Vector3 low = start + Vector3.up * (vaultMinHeight * .7f);
        if (!Physics.Raycast(low, forward, out RaycastHit obstruction, vaultProbeDistance,
                worldMask, QueryTriggerInteraction.Ignore)) return false;
        if (obstruction.collider.transform.root == transform.root) return false;
        if (Mathf.Abs(obstruction.normal.y) > .35f) return false;

        // A downward ray just past the obstacle seeks a safe, flat top surface.
        Vector3 topOrigin = obstruction.point + forward * .32f +
            Vector3.up * (vaultMaxHeight + .45f);
        if (!Physics.Raycast(topOrigin, Vector3.down, out RaycastHit top,
                vaultMaxHeight + .7f, worldMask, QueryTriggerInteraction.Ignore)) return false;
        if (top.normal.y < .68f) return false;
        float rise = top.point.y - start.y;
        if (rise < vaultMinHeight || rise > vaultMaxHeight) return false;

        // Clear space ahead at head height, otherwise do not vault into a wall.
        Vector3 upperOrigin = start + Vector3.up * (vaultMaxHeight + .65f);
        if (Physics.Raycast(upperOrigin, forward, vaultProbeDistance + .5f,
                worldMask, QueryTriggerInteraction.Ignore)) return false;

        vaultStart = start;
        vaultEnd = top.point + forward * vaultBeyondDistance;
        // Land slightly above the target surface so controller collision resolves down.
        vaultEnd.y = top.point.y + .08f;
        vaultElapsed = 0f;
        State = MotionState.Vaulting;
        desiredCrouch = false;
        jumpBufferTimer = 0f;
        verticalVelocity = 0f;
        return true;
    }

    private void SimulateVault(float dt)
    {
        vaultElapsed += dt;
        float progress = Mathf.Clamp01(vaultElapsed / Mathf.Max(.01f, vaultDuration));
        float eased = progress * progress * (3f - 2f * progress);
        Vector3 desiredPosition = Vector3.Lerp(vaultStart, vaultEnd, eased);
        desiredPosition.y += Mathf.Sin(progress * Mathf.PI) * vaultArcHeight;
        Vector3 delta = desiredPosition - transform.position;
        CollisionFlags collision = characterController.Move(delta);
        if ((collision & CollisionFlags.Sides) != 0 && progress < .95f)
        {
            State = MotionState.Airborne;
            verticalVelocity = 0f;
        }
        if (progress >= 1f)
        {
            State = characterController.isGrounded ? MotionState.Grounded : MotionState.Airborne;
            // Preserve some forward flow into the next action.
            Vector3 direction = Vector3.ProjectOnPlane(vaultEnd - vaultStart, Vector3.up).normalized;
            planarVelocity = direction * Mathf.Max(walkSpeed, planarVelocity.magnitude);
            verticalVelocity = -1f;
        }
        UpdateStance(MoveCommand.Empty, dt);
    }

    private void PostMoveCollision(CollisionFlags flags, bool wasGrounded)
    {
        if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
            verticalVelocity = 0f;
        if ((flags & CollisionFlags.Below) != 0)
        {
            if (verticalVelocity < 0f) verticalVelocity = -2f;
            if (!wasGrounded)
            {
                airDashesRemaining = airDashesPerJump;
                wallTimer = 0f;
                wallDetachUntil = 0f;
            }
            if (State == MotionState.Airborne || State == MotionState.WallRunning ||
                State == MotionState.WallClimbing)
                State = MotionState.Grounded;
        }
    }

    private void UpdateStance(MoveCommand cmd, float dt)
    {
        bool shouldCrouch = allowCrouching && (cmd.CrouchHeld ||
            State == MotionState.Sliding || (standBlocked && !CanStand()));
        float targetHeight = shouldCrouch ? crouchHeight : standingHeight;
        if (!shouldCrouch && !CanStand())
        {
            targetHeight = crouchHeight;
            standBlocked = true;
        }
        else standBlocked = false;
        if (!shouldCrouch && !standBlocked && State == MotionState.Crouching)
            State = Grounded ? MotionState.Grounded : MotionState.Airborne;
        if (standBlocked && characterController.isGrounded && State == MotionState.Grounded)
            State = MotionState.Crouching;

        // Preserve controller feet position while changing standing height.
        float newHeight = Mathf.MoveTowards(characterController.height,
            targetHeight, stanceTransitionSpeed * dt);
        if (Mathf.Abs(newHeight - characterController.height) > .0001f)
        {
            characterController.height = newHeight;
            characterController.center = standingCenter +
                Vector3.up * ((newHeight - standingHeight) * .5f);
        }
    }

    private bool CanStand()
    {
        if (characterController.height >= standingHeight - .01f) return true;
        float radius = Mathf.Max(.01f, characterController.radius - characterController.skinWidth);
        // Check only the *newly occupied head room*, not the whole standing
        // capsule. A full-body overlap can incorrectly see the floor as a blocker.
        Vector3 currentCenter = transform.TransformPoint(characterController.center);
        Vector3 targetCenter = transform.TransformPoint(standingCenter);
        float currentTop = currentCenter.y + characterController.height * .5f;
        float targetTop = targetCenter.y + standingHeight * .5f;
        Vector3 a = new Vector3(currentCenter.x, currentTop - radius * .35f, currentCenter.z);
        Vector3 b = new Vector3(targetCenter.x, targetTop - radius, targetCenter.z);
        if (b.y < a.y) b.y = a.y;
        int count = Physics.OverlapCapsuleNonAlloc(a, b, radius, clearanceResults,
            worldMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider c = clearanceResults[i];
            if (c != null && c.transform.root != transform.root)
                return false;
        }
        return true;
    }

    private void RotateBody(Vector3 wish, float dt)
    {
        if (State == MotionState.Dashing) return;
        if (!orientBodyToMove || State == MotionState.WallClimbing) return;
        Vector3 facing = wish;
        if (orientToView && cameraTransform != null)
            facing = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
        if (facing.sqrMagnitude < .01f) return;
        Quaternion desired = Quaternion.LookRotation(facing.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation,
            desired, turnSpeed * dt);
    }

    public void SetOrientationToView(bool value) => orientToView = value;

    // ---- Existing BotSwitcher and SettingsUI compatibility ----
    public void ApplyLocalState()
    {
        bool active = !replicatedProxy && !externalSimulation && IsLocallyControlled && gameplayInputAllowed;
        if (commands is IMoveInputGate inputGate) inputGate.InputAllowed = active;
    }

    public void SetInputAllowed(bool allowed)
    {
        gameplayInputAllowed = allowed;
        ApplyLocalState();
    }

    public void SetLocalPlayer(bool local)
    {
        localPlayer = local;
        ApplyLocalState();
    }

    public void SetCamera(Transform newCamera)
    {
        cameraTransform = newCamera;
        if (cameraRig != null) cameraRig.SetCamera(newCamera);
    }

    public void SetLookSensitivity(float x, float y)
    {
        if (cameraRig != null) cameraRig.SetLookSensitivity(x, y);
    }

    public void SetInvertY(bool value)
    {
        if (cameraRig != null) cameraRig.SetInvertY(value);
    }

    // ---- Host/server/AI bridge: external systems supply commands at their own cadence. ----
    public void UseExternalSimulation(bool value)
    {
        externalSimulation = value;
        ApplyLocalState();
    }

    public void SubmitExternalCommand(MoveCommand command)
    {
        // Called by an authority adapter before this motor's Update.
        externalCommand = command;
        hasExternalCommand = true;
    }

    public void SetReplicatedProxy(bool value)
    {
        replicatedProxy = value;
        ApplyLocalState();
    }

    public void ApplyNetworkSnapshot(Vector3 position, Quaternion rotation)
    {
        // Intended only for a non-simulated remote proxy. Interpolate in your
        // network adapter, not inside this gameplay motor.
        if (!replicatedProxy) return;
        bool active = characterController.enabled;
        characterController.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        characterController.enabled = active;
    }

    public void ResetMotion()
    {
        planarVelocity = Vector3.zero;
        verticalVelocity = 0f;
        sprintProgress = 0f;
        jumpBufferTimer = 0f;
        State = MotionState.Airborne;
    }
}

// These small data contracts are co-located in SimpleMove.cs intentionally.
// Do NOT import an additional MoveCommand.cs from older versions with this file.
public struct MoveCommand
{
    public Vector2 Move;
    public Vector2 Look;
    public bool JumpPressed;
    public bool JumpHeld;
    public bool SprintHeld;
    public bool CrouchHeld;
    public bool CrouchPressed;
    public bool SlidePressed;
    public bool DashPressed;
    public bool ClimbHeld;
    public bool ToggleViewPressed;
    public static MoveCommand Empty => default(MoveCommand);
}

public interface IMoveCommandSource
{
    MoveCommand SampleCommand();
}

public interface IMoveInputGate
{
    bool InputAllowed { get; set; }
}

public interface IMoveCameraRig
{
    void SetCamera(Transform newCamera);
    void SetLookSensitivity(float x, float y);
    void SetInvertY(bool value);
}
