using System;

public enum MovementStateId
{
    Idle, Walking, Sprinting, Jumping, Falling, Crouching, Stunned
}

public interface IMovementState
{
    MovementStateId Current { get; }

    // Giá trị hiện tại (thô)
    float Stamina { get; }

    // Giá trị tối đa để UI tự chuẩn hóa
    float MaxStamina { get; }

    /// <summary> Raised when Current changes. from=prev, to=new </summary>
    event Action<MovementStateId, MovementStateId> OnStateChanged;
}