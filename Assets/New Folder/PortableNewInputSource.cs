#if ENABLE_INPUT_SYSTEM
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Optional Input System keyboard+mouse source. Add to the SAME object as SimpleMove.
/// Works with Active Input Handling = Input System Package or Both.</summary>
[DisallowMultipleComponent]
public sealed class PortableNewInputSource : MonoBehaviour, IMoveCommandSource, IMoveInputGate
{
    [Tooltip("Mouse delta pixels-to-look scaling; camera rig handles sensitivity separately.")]
    [SerializeField, Min(0f)] private float mouseDeltaScale = 0.025f;
    public bool InputAllowed { get; set; } = true;

    public MoveCommand SampleCommand()
    {
        MoveCommand cmd = MoveCommand.Empty;
        Keyboard kb = Keyboard.current;
        if (!InputAllowed || kb == null) return cmd;
        cmd.Move = Vector2.ClampMagnitude(new Vector2(
            (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f),
            (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f)), 1f);
        cmd.Look = Mouse.current != null ? Mouse.current.delta.ReadValue() * mouseDeltaScale : Vector2.zero;
        cmd.JumpPressed = kb.spaceKey.wasPressedThisFrame;
        cmd.JumpHeld = kb.spaceKey.isPressed;
        cmd.SprintHeld = kb.leftShiftKey.isPressed;
        cmd.CrouchHeld = kb.leftCtrlKey.isPressed;
        cmd.CrouchPressed = kb.leftCtrlKey.wasPressedThisFrame;
        cmd.SlidePressed = kb.cKey.wasPressedThisFrame;
        cmd.DashPressed =
        kb.qKey.wasPressedThisFrame || (Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame);
        cmd.ClimbHeld = kb.spaceKey.isPressed;
        cmd.ToggleViewPressed = kb.vKey.wasPressedThisFrame;
        return cmd;
    }
}
#endif
