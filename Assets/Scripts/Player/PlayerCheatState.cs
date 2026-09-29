using UnityEngine;

namespace GetLost.Cheats
{
    /// <summary>
    /// Authoritative player cheat flags that gameplay systems can query without depending
    /// on the cheat-menu UI. Future damage receivers should call PreventsDamage first.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Get Lost/Debug/Player Cheat State")]
    public sealed class PlayerCheatState : MonoBehaviour
    {
        [SerializeField] private bool invincible;

        public bool IsInvincible => invincible;

        public void SetInvincible(bool value) => invincible = value;

        public static bool PreventsDamage(Component target)
        {
            if (!target)
                return false;
            PlayerCheatState state = target.GetComponentInParent<PlayerCheatState>();
            return state && state.IsInvincible;
        }
    }
}
