using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// Replace the existing PortableNewInputSource. Attach to Player alongside SimpleMove.
/// Keyboard/mouse commands use saved bindings under either Unity input backend.
[DisallowMultipleComponent]
public sealed class PortableNewInputSource : MonoBehaviour, IMoveCommandSource, IMoveInputGate
{
    [SerializeField, Min(0f)] private float mouseDeltaScale = .025f;
    public bool InputAllowed { get; set; } = true;
    public MoveCommand SampleCommand()
    {
        MoveCommand cmd=MoveCommand.Empty;
        if(!InputAllowed || PlayerControlSettings.GameplayBlocked) return cmd;
        cmd.Move=PlayerControlSettings.Movement;
#if ENABLE_INPUT_SYSTEM
        cmd.Look=Mouse.current!=null ? Mouse.current.delta.ReadValue()*mouseDeltaScale : Vector2.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
        cmd.Look=new Vector2(Input.GetAxisRaw("Mouse X"),Input.GetAxisRaw("Mouse Y"));
#endif
        cmd.JumpPressed=PlayerControlSettings.Pressed(PlayerControlSettings.Control.Jump);
        cmd.JumpHeld=PlayerControlSettings.Held(PlayerControlSettings.Control.Jump);
        cmd.SprintHeld=PlayerControlSettings.Held(PlayerControlSettings.Control.Sprint);
        cmd.CrouchHeld=PlayerControlSettings.Held(PlayerControlSettings.Control.Crouch);
        cmd.CrouchPressed=PlayerControlSettings.Pressed(PlayerControlSettings.Control.Crouch);
        cmd.SlidePressed=PlayerControlSettings.Pressed(PlayerControlSettings.Control.Slide);
        cmd.DashPressed=PlayerControlSettings.Pressed(PlayerControlSettings.Control.Dash);
        cmd.ClimbHeld=PlayerControlSettings.Held(PlayerControlSettings.Control.Climb);
        cmd.ToggleViewPressed=PlayerControlSettings.Pressed(PlayerControlSettings.Control.ChangeView);
        return cmd;
    }
}
