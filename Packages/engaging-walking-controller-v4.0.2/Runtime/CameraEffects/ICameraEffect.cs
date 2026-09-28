namespace Tom.WalkingController
{
    /// <summary>
    /// Implemented by procedural camera modules. Modules return offsets only;
    /// CameraEffectsController is the sole component allowed to move the effects pivot.
    /// </summary>
    public interface ICameraEffect
    {
        bool CameraEffectEnabled { get; }
        CameraEffectFrame EvaluateCameraEffect(float deltaTime);
    }
}
