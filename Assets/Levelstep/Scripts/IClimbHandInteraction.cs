namespace Levelstep
{
    /// <summary>Explicit hand identity and optional drain policy for accessibility climb interactions.</summary>
    public interface IClimbHandInteraction
    {
        ClimbStaminaController.HandSide HandSide { get; }
        bool ConsumesStamina { get; }
    }
}
