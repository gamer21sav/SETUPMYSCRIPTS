using UnityEngine;
[DisallowMultipleComponent, DefaultExecutionOrder(100)]
public sealed class PlayerCameraRig : MonoBehaviour, IMoveCameraRig
{
    public enum ViewMode { FirstPerson, ThirdPerson }
    [SerializeField] private Transform cameraTransform;
    [Tooltip("Optional camera pivot placed on the Player root, outside the animated model. Position it at the character's eye height.")]
    [SerializeField] private Transform cameraFollowTarget;
    [Tooltip("Optional first-person camera position attached to the animated model's head/eye area.")]
    [SerializeField] private Transform firstPersonFollowTarget;
    [SerializeField] private ViewMode startingView = ViewMode.ThirdPerson;
    [SerializeField] private bool allowViewToggle = true;
    [SerializeField, Min(0f)] private float sensitivityX = 2f, sensitivityY = 2f;
    [SerializeField] private bool invertY;
    [SerializeField, Range(-89f, 0f)] private float minimumPitch = -75f;
    [SerializeField, Range(0f, 89f)] private float maximumPitch = 80f;
    [SerializeField, Min(.1f)] private float followDistance = 3.3f;
    [SerializeField, Min(0f)] private float shoulderOffset = .38f, positionSmoothTime = .055f;
    [SerializeField, Min(.02f)] private float collisionRadius = .2f;
    [SerializeField, Min(0f)] private float wallOffset = .1f;
    [SerializeField] private LayerMask collisionMask = ~0;
    [SerializeField] private float standingEyeHeight = 1.66f, crouchingEyeHeight = 1f;
    [SerializeField, Min(0f)] private float eyeSmoothSpeed = 10f;
    private SimpleMove motor;
    private float yaw, pitch, eyeHeight, armLength, armVelocity;
    private readonly RaycastHit[] collisionHits = new RaycastHit[32];
    public ViewMode Mode { get; private set; }
    public Transform CameraTransform => cameraTransform;
    public bool IsFirstPerson => Mode == ViewMode.FirstPerson;
    private void Awake()
    {
        motor = GetComponent<SimpleMove>(); eyeHeight = standingEyeHeight;
        if (!cameraTransform) { Camera own = GetComponentInChildren<Camera>(true); if (own) cameraTransform = own.transform; }
        SetCamera(cameraTransform); SetMode(startingView);
    }
    private void LateUpdate()
    {
        if (!motor || !cameraTransform || !motor.IsLocallyControlled || !motor.IsSimulating) return;
        MoveCommand command = motor.LastCommand;
        if (motor.GameplayInputAllowed && Application.isFocused && Cursor.lockState == CursorLockMode.Locked) {
            if (allowViewToggle && command.ToggleViewPressed) SetMode(IsFirstPerson ? ViewMode.ThirdPerson : ViewMode.FirstPerson);
            yaw = Mathf.Repeat(yaw + command.Look.x * sensitivityX, 360f);
            pitch = Mathf.Clamp(pitch + command.Look.y * sensitivityY * (invertY ? 1f : -1f), minimumPitch, maximumPitch);
        }
        bool crouched = motor.State == SimpleMove.MotionState.Crouching || motor.State == SimpleMove.MotionState.Sliding;
        eyeHeight = Mathf.MoveTowards(eyeHeight, crouched ? crouchingEyeHeight : standingEyeHeight, eyeSmoothSpeed * Time.deltaTime);
        Vector3 pivot = IsFirstPerson && firstPersonFollowTarget
            ? firstPersonFollowTarget.position
            : cameraFollowTarget
            ? cameraFollowTarget.position + Vector3.up * (eyeHeight - standingEyeHeight)
            : transform.position + Vector3.up * eyeHeight;
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        if (IsFirstPerson) { cameraTransform.SetPositionAndRotation(pivot, rotation); return; }
        Vector3 offset = rotation * new Vector3(shoulderOffset, 0f, -Mathf.Max(.1f, followDistance));
        float target = offset.magnitude; Vector3 direction = offset / target;
        int count = Physics.SphereCastNonAlloc(pivot, Mathf.Max(.02f, collisionRadius), direction, collisionHits, target, collisionMask, QueryTriggerInteraction.Ignore);
        RaycastHit[] hits = collisionHits;
        if (count == hits.Length) { hits = Physics.SphereCastAll(pivot, Mathf.Max(.02f, collisionRadius), direction, target, collisionMask, QueryTriggerInteraction.Ignore); count = hits.Length; }
        for (int i = 0; i < count; i++) if (hits[i].collider && !hits[i].collider.transform.IsChildOf(transform)) target = Mathf.Min(target, Mathf.Max(0f, hits[i].distance - wallOffset));
        // Follow the pivot exactly; never smooth from a camera position displaced by parent rotation.
        if (target <= armLength || positionSmoothTime <= .001f) { armLength = target; armVelocity = 0f; }
        else armLength = Mathf.Min(target, Mathf.SmoothDamp(armLength, target, ref armVelocity, positionSmoothTime));
        cameraTransform.SetPositionAndRotation(pivot + direction * armLength, rotation);
    }
    public void SetMode(ViewMode mode) { Mode = mode; armLength = followDistance; armVelocity = 0f; if (motor) motor.SetOrientationToView(IsFirstPerson); }
    public void SetCamera(Transform value) { cameraTransform = value; yaw = value ? value.eulerAngles.y : transform.eulerAngles.y; pitch = value ? Mathf.Clamp(Mathf.DeltaAngle(0f, value.eulerAngles.x), minimumPitch, maximumPitch) : 0f; armLength = followDistance; armVelocity = 0f; }
    public void SetLookSensitivity(float x, float y) { sensitivityX = Mathf.Max(0f, x); sensitivityY = Mathf.Max(0f, y); }
    public void SetInvertY(bool value) => invertY = value;
}
