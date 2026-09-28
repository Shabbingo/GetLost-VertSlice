using UnityEngine;

namespace Tom.WalkingController
{
    /// <summary>A single frame of additive procedural camera motion.</summary>
    public readonly struct CameraEffectFrame
    {
        public readonly Vector3 Position;
        public readonly Vector3 RotationEuler;

        public CameraEffectFrame(Vector3 position, Vector3 rotationEuler)
        {
            Position = position;
            RotationEuler = rotationEuler;
        }

        public static CameraEffectFrame None => new(Vector3.zero, Vector3.zero);

        public static CameraEffectFrame operator +(CameraEffectFrame a, CameraEffectFrame b)
        {
            return new CameraEffectFrame(a.Position + b.Position, a.RotationEuler + b.RotationEuler);
        }
    }
}
