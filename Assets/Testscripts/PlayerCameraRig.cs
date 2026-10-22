using UnityEngine;

/// <summary>
/// One camera for first OR third person, local-player only.
/// Place this on the Player root alongside SimpleMove. Finds the Player's OWN
/// child Camera; never uses Camera.main. BotSwitcher may enable its Camera,
/// AudioListener and HUD without enabling other players' camera controls.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerCameraRig : MonoBehaviour, IMoveCameraRig
{
    public enum ViewMode { FirstPerson, ThirdPerson }

    [Header("Optional Camera Reference")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private ViewMode startingView = ViewMode.ThirdPerson;
    [SerializeField] private bool allowViewToggle = true;

    [Header("Look")]
    [SerializeField, Min(0f)] private float sensitivityX = 2f;
    [SerializeField, Min(0f)] private float sensitivityY = 2f;
    [SerializeField] private bool invertY;
    [SerializeField, Range(-89f, 0f)] private float minimumPitch = -75f;
    [SerializeField, Range(0f, 89f)] private float maximumPitch = 80f;

    [Header("Third Person")]
    [SerializeField, Min(.1f)] private float followDistance = 3.3f;
    [SerializeField, Min(0f)] private float shoulderOffset = .38f;
    [SerializeField, Min(0f)] private float positionSmoothTime = .055f;
    [SerializeField, Min(.02f)] private float collisionRadius = .2f;
    [SerializeField, Min(0f)] private float wallOffset = .1f;
    [SerializeField] private LayerMask collisionMask = ~0;

    [Header("Camera Pivot")]
    [SerializeField] private float standingEyeHeight = 1.66f;
    [SerializeField] private float crouchingEyeHeight = 1.0f;
    [SerializeField, Min(0f)] private float eyeSmoothSpeed = 10f;

    private SimpleMove motor;
    private float yaw;
    private float pitch;
    private float eyeHeight;
    private Vector3 positionVelocity;
    private readonly RaycastHit[] collisionHits = new RaycastHit[16];

    public ViewMode Mode { get; private set; }
    public Transform CameraTransform => cameraTransform;
    public bool IsFirstPerson => Mode == ViewMode.FirstPerson;

    private void Awake()
    {
        motor = GetComponent<SimpleMove>();
        if (cameraTransform == null)
        {
            Camera ownCamera = GetComponentInChildren<Camera>(true);
            if (ownCamera != null) cameraTransform = ownCamera.transform;
        }
        Mode = startingView;
        eyeHeight = standingEyeHeight;
        yaw = cameraTransform != null ? cameraTransform.eulerAngles.y : transform.eulerAngles.y;
        pitch = cameraTransform != null ? Mathf.DeltaAngle(0f, cameraTransform.eulerAngles.x) : 0f;
        if (motor != null) motor.SetOrientationToView(IsFirstPerson);
    }

    private void LateUpdate()
    {
        if (motor == null || cameraTransform == null) return;
        if (!motor.IsLocallyControlled) return;
        if (!motor.GameplayInputAllowed || !motor.IsSimulating) return;

        MoveCommand command = motor.LastCommand;
        if (allowViewToggle && command.ToggleViewPressed)
            SetMode(IsFirstPerson ? ViewMode.ThirdPerson : ViewMode.FirstPerson);

        yaw += command.Look.x * sensitivityX;
        pitch += command.Look.y * sensitivityY * (invertY ? 1f : -1f);
        pitch = Mathf.Clamp(pitch, minimumPitch, maximumPitch);

        Quaternion lookRotation = Quaternion.Euler(pitch, yaw, 0f);
        bool crouched = motor.State == SimpleMove.MotionState.Crouching ||
                        motor.State == SimpleMove.MotionState.Sliding;
        float goalEye = crouched ? crouchingEyeHeight : standingEyeHeight;
        eyeHeight = Mathf.MoveTowards(eyeHeight, goalEye,
            eyeSmoothSpeed * Time.deltaTime);
        Vector3 pivot = transform.position + Vector3.up * eyeHeight;

        if (IsFirstPerson)
        {
            positionVelocity = Vector3.zero;
            cameraTransform.SetPositionAndRotation(pivot, lookRotation);
            return;
        }

        Vector3 offset = lookRotation * new Vector3(shoulderOffset, 0f, -followDistance);
        Vector3 desired = pivot + offset;
        Vector3 path = desired - pivot;
        float distance = path.magnitude;

        if (distance > .001f)
        {
            int count = Physics.SphereCastNonAlloc(pivot, collisionRadius,
                path / distance, collisionHits, distance, collisionMask,
                QueryTriggerInteraction.Ignore);
            float closest = distance;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = collisionHits[i];
                if (hit.collider == null || hit.collider.transform.root == transform.root) continue;
                closest = Mathf.Min(closest, hit.distance);
            }
            if (closest < distance)
                desired = pivot + path.normalized * Mathf.Max(.05f, closest - wallOffset);
        }

        Vector3 newPosition = positionSmoothTime <= .001f
            ? desired
            : Vector3.SmoothDamp(cameraTransform.position, desired, ref positionVelocity,
                positionSmoothTime);
        cameraTransform.SetPositionAndRotation(newPosition, lookRotation);
    }

    public void SetMode(ViewMode mode)
    {
        Mode = mode;
        positionVelocity = Vector3.zero;
        if (motor != null) motor.SetOrientationToView(IsFirstPerson);
    }

    public void SetCamera(Transform newCamera)
    {
        cameraTransform = newCamera;
        if (cameraTransform != null)
        {
            yaw = cameraTransform.eulerAngles.y;
            pitch = Mathf.DeltaAngle(0f, cameraTransform.eulerAngles.x);
        }
        positionVelocity = Vector3.zero;
    }

    public void SetLookSensitivity(float x, float y)
    {
        sensitivityX = Mathf.Max(0f, x);
        sensitivityY = Mathf.Max(0f, y);
    }

    public void SetInvertY(bool value) => invertY = value;
}
