using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Chases a target with NavMesh path planning and drives the existing SimpleMove
/// motor. The motor performs the actual movement, dash, wall run, climb, vault,
/// and mantle. Put this script beside NavMeshAgent, SimpleMove, and the capsule.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)] // Submit commands before SimpleMove.Update.
[RequireComponent(typeof(NavMeshAgent), typeof(SimpleMove))]
public sealed class ParkourChaseAI : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private string targetTag = "Player";
    [SerializeField, Min(0.1f)] private float retargetInterval = 1f;
    [SerializeField, Min(0f)] private float stopDistance = 2f;

    [Header("Navigation")]
    [SerializeField, Min(0.1f)] private float walkSpeed = 3.5f;
    [SerializeField, Min(0.1f)] private float runSpeed = 6f;
    [SerializeField, Min(0.05f)] private float destinationRefreshDistance = 0.6f;
    [SerializeField] private LayerMask obstacleMask = ~0;

    [Header("Parkour")]
    [SerializeField] private bool useDash = true;
    [SerializeField, Min(0.1f)] private float dashInterval = 2.8f;
    [SerializeField, Min(0f)] private float dashOnlyWhenFartherThan = 7f;
    [SerializeField, Min(0.1f)] private float obstacleProbeDistance = 1.35f;
    [SerializeField] private bool jumpToStartWallRun = true;

    [Header("Animator parameter names")]
    [SerializeField] private Animator animator;
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private string moveXParameter = "MoveX";
    [SerializeField] private string moveYParameter = "MoveY";
    [SerializeField] private string movingParameter = "Ismove";
    [SerializeField] private string groundedParameter = "Grounded";
    [SerializeField] private string jumpingParameter = "Jumping";
    [SerializeField] private string fallingParameter = "Falling";
    [SerializeField] private string wallRunParameter = "WallRunning";
    [SerializeField] private string climbingParameter = "Climbing";
    [SerializeField] private string vaultingParameter = "Vaulting";
    [SerializeField] private string mantlingParameter = "Mantling";
    [SerializeField, Min(0f)] private float animationDampTime = 0.1f;

    private NavMeshAgent planner;
    private SimpleMove motor;
    private CharacterController capsule;
    private readonly HashSet<int> floatParameters = new HashSet<int>();
    private readonly HashSet<int> boolParameters = new HashSet<int>();
    private float nextTargetSearch;
    private float nextDashTime;
    private Vector3 lastDestination;
    private bool hasDestination;

    private void Awake()
    {
        planner = GetComponent<NavMeshAgent>();
        motor = GetComponent<SimpleMove>();
        capsule = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>(true);

        // NavMeshAgent computes a path only. SimpleMove owns the transform.
        planner.updatePosition = false;
        planner.updateRotation = false;
        planner.speed = runSpeed;
        planner.stoppingDistance = stopDistance;
        motor.SetCamera(transform);
        motor.SetLocalPlayer(false);
        motor.SetInputAllowed(true);
        motor.UseExternalSimulation(true);
        if (animator != null) animator.applyRootMotion = false;
        CacheAnimatorParameters();
    }

    private void OnEnable()
    {
        if (motor != null) motor.UseExternalSimulation(true);
    }

    private void OnDisable()
    {
        if (motor != null)
        {
            motor.UseExternalSimulation(false);
            motor.SetInputAllowed(false);
        }
        if (planner != null && planner.enabled && planner.isOnNavMesh)
            planner.isStopped = true;
    }

    private void Update()
    {
        if (planner == null || motor == null || !planner.enabled || !planner.isOnNavMesh)
            return;

        // Keep the planner attached to the position moved by CharacterController.
        planner.nextPosition = transform.position;
        FindTargetIfNeeded();
        if (target == null)
        {
            planner.isStopped = true;
            motor.SubmitExternalCommand(MoveCommand.Empty);
            return;
        }

        float targetDistance = PlanarDistance(transform.position, target.position);
        planner.speed = targetDistance < 9f ? runSpeed : walkSpeed;
        planner.stoppingDistance = stopDistance;
        planner.isStopped = targetDistance <= stopDistance;

        if (!planner.isStopped &&
            (!hasDestination || (target.position - lastDestination).sqrMagnitude >=
             destinationRefreshDistance * destinationRefreshDistance))
        {
            hasDestination = planner.SetDestination(target.position);
            lastDestination = target.position;
        }

        if (planner.isStopped)
        {
            FaceTarget();
            motor.SubmitExternalCommand(MoveCommand.Empty);
            return;
        }

        Vector3 worldMove = planner.desiredVelocity;
        worldMove.y = 0f;
        if (worldMove.sqrMagnitude < 0.01f)
        {
            worldMove = target.position - transform.position;
            worldMove.y = 0f;
        }
        Vector3 localMove = transform.InverseTransformDirection(worldMove.normalized);
        Vector2 move = Vector2.ClampMagnitude(new Vector2(localMove.x, localMove.z), 1f);

        bool obstacleAhead = HasWallAhead(worldMove.normalized, out RaycastHit obstacle);
        bool sideWall = HasSideWall();
        bool onGround = motor.Grounded;
        bool climbing = motor.State == SimpleMove.MotionState.WallClimbing;
        bool pressDash = useDash && targetDistance > dashOnlyWhenFartherThan &&
                         Time.time >= nextDashTime && move.sqrMagnitude > 0.1f;
        if (pressDash) nextDashTime = Time.time + Mathf.Max(0.1f, dashInterval);

        MoveCommand command = new MoveCommand
        {
            Move = move,
            SprintHeld = true,
            JumpHeld = true,
            // Low obstacles use the motor's vault. Taller ledges use its mantle.
            JumpPressed = onGround && (obstacleAhead || (jumpToStartWallRun && sideWall)),
            MantlePressed = obstacleAhead && (onGround || climbing),
            ClimbHeld = obstacleAhead && !onGround,
            DashPressed = pressDash
        };
        motor.SubmitExternalCommand(command);
    }

    private bool HasWallAhead(Vector3 direction, out RaycastHit hit)
    {
        Vector3 origin = transform.position + Vector3.up *
            (capsule != null ? Mathf.Min(capsule.height * 0.55f, 1.1f) : 0.9f);
        if (direction.sqrMagnitude < 0.01f)
        {
            hit = default(RaycastHit);
            return false;
        }
        return Physics.Raycast(origin, direction, out hit, obstacleProbeDistance,
            obstacleMask, QueryTriggerInteraction.Ignore) &&
            hit.collider.transform.root != transform.root && Mathf.Abs(hit.normal.y) < 0.35f;
    }

    private bool HasSideWall()
    {
        Vector3 origin = transform.position + Vector3.up *
            (capsule != null ? Mathf.Min(capsule.height * 0.55f, 1.1f) : 0.9f);
        float distance = 1f;
        return Physics.Raycast(origin, transform.right, distance, obstacleMask,
                   QueryTriggerInteraction.Ignore) ||
               Physics.Raycast(origin, -transform.right, distance, obstacleMask,
                   QueryTriggerInteraction.Ignore);
    }

    private void FindTargetIfNeeded()
    {
        if (target != null || Time.time < nextTargetSearch || string.IsNullOrEmpty(targetTag)) return;
        nextTargetSearch = Time.time + Mathf.Max(0.1f, retargetInterval);
        try
        {
            GameObject found = GameObject.FindGameObjectWithTag(targetTag);
            if (found != null && found.transform.root != transform.root) target = found.transform;
        }
        catch (UnityException)
        {
            Debug.LogWarning($"{name}: target tag '{targetTag}' does not exist. Assign Target in the Inspector.", this);
            targetTag = string.Empty;
        }
    }

    private void FaceTarget()
    {
        Vector3 direction = target.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) return;
        Quaternion rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, rotation, 360f * Time.deltaTime);
    }

    private void LateUpdate()
    {
        if (animator == null || motor == null || !animator.isActiveAndEnabled) return;
        Vector3 velocity = motor.Velocity;
        Vector3 localVelocity = transform.InverseTransformDirection(velocity);
        Vector2 direction = new Vector2(localVelocity.x, localVelocity.z);
        if (direction.sqrMagnitude > 1f) direction.Normalize();
        float speed = new Vector2(velocity.x, velocity.z).magnitude;

        SetFloat(speedParameter, speed);
        SetFloat(moveXParameter, direction.x);
        SetFloat(moveYParameter, direction.y);
        SetBool(movingParameter, speed > 0.1f);
        SetBool(groundedParameter, motor.Grounded);
        SetBool(jumpingParameter, motor.Velocity.y > 0.15f);
        SetBool(fallingParameter, !motor.Grounded && motor.Velocity.y < -0.15f);
        SetBool(wallRunParameter, motor.State == SimpleMove.MotionState.WallRunning);
        SetBool(climbingParameter, motor.State == SimpleMove.MotionState.WallClimbing);
        SetBool(vaultingParameter, motor.State == SimpleMove.MotionState.Vaulting);
        SetBool(mantlingParameter, motor.State == SimpleMove.MotionState.Mantling);
    }

    private void CacheAnimatorParameters()
    {
        floatParameters.Clear();
        boolParameters.Clear();
        if (animator == null) return;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            int hash = Animator.StringToHash(parameter.name);
            if (parameter.type == AnimatorControllerParameterType.Float) floatParameters.Add(hash);
            if (parameter.type == AnimatorControllerParameterType.Bool) boolParameters.Add(hash);
        }
    }

    private void SetFloat(string parameter, float value)
    {
        if (string.IsNullOrEmpty(parameter) || animator == null) return;
        int hash = Animator.StringToHash(parameter);
        if (floatParameters.Contains(hash))
            animator.SetFloat(hash, value, animationDampTime, Time.deltaTime);
    }

    private void SetBool(string parameter, bool value)
    {
        if (string.IsNullOrEmpty(parameter) || animator == null) return;
        int hash = Animator.StringToHash(parameter);
        if (boolParameters.Contains(hash)) animator.SetBool(hash, value);
    }

    private static float PlanarDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}