using System.Collections;
using GetLost.Game;
using Tom.WalkingController;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace GetLost.Player
{
    /// <summary>Installs the prototype health, tumble and global damage-volume stack.</summary>
    public sealed class PlayerDamageSystemBootstrap : MonoBehaviour
    {
        private const string HealthProfileResource = "GetLost/HealthEffectsVolumeProfile";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (FindAnyObjectByType<PlayerDamageSystemBootstrap>() == null)
                new GameObject("Player Damage System Setup").AddComponent<PlayerDamageSystemBootstrap>();
        }

        private IEnumerator Start()
        {
            WalkingMotor motor = null;
            float giveUpAt = Time.realtimeSinceStartup + 10f;
            while (motor == null && Time.realtimeSinceStartup < giveUpAt)
            {
                motor = FindAnyObjectByType<WalkingMotor>();
                if (motor == null)
                    yield return null;
            }
            if (motor == null)
                yield break;

            InstallOnPlayer(motor);
        }

        private static void InstallOnPlayer(WalkingMotor motor)
        {
            GameObject player = motor.gameObject;
            PlayerDamageController damage = player.GetComponent<PlayerDamageController>() ??
                                              player.AddComponent<PlayerDamageController>();

            if (player.GetComponent<Rigidbody>() == null)
                player.AddComponent<Rigidbody>();
            if (player.GetComponent<CapsuleCollider>() == null)
                player.AddComponent<CapsuleCollider>();

            PlayerFallController fall = player.GetComponent<PlayerFallController>() ??
                                        player.AddComponent<PlayerFallController>();
            Camera playerCamera = motor.GetComponentInChildren<Camera>(true);
            FirstPersonLook look = motor.GetComponentInChildren<FirstPersonLook>(true);
            fall.ConfigureForPlayer(playerCamera != null ? playerCamera.transform : null, look);

            Volume damageVolume = EnsureDamageVolume(player.transform);
            ConfigureCameraPostProcessing(playerCamera, damageVolume);
            Camera spectatorCamera = EnsureSpectatorCamera(player.transform, playerCamera, damageVolume);
            AudioListener spectatorListener = spectatorCamera != null
                ? spectatorCamera.GetComponent<AudioListener>()
                : null;
            fall.ConfigureSpectatorCamera(spectatorCamera, spectatorListener);
            PlayerDamageVisualController visuals = player.GetComponent<PlayerDamageVisualController>() ??
                                                    player.AddComponent<PlayerDamageVisualController>();
            visuals.Initialize(damage, damageVolume);

            GameManager manager = FindAnyObjectByType<GameManager>();
            if (manager == null)
                manager = new GameObject("Game Manager").AddComponent<GameManager>();
            manager.SetPlayerDamageController(damage);
        }

        private static Volume EnsureDamageVolume(Transform player)
        {
            Transform existing = player.Find("HealthEffectsVolume");
            GameObject volumeObject = existing != null
                ? existing.gameObject
                : new GameObject("HealthEffectsVolume");
            volumeObject.transform.SetParent(player, false);

            Volume volume = volumeObject.GetComponent<Volume>() ?? volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.weight = 0f;
            VolumeProfile profile = Resources.Load<VolumeProfile>(HealthProfileResource);
            if (profile != null)
                volume.sharedProfile = profile;
            else
                Debug.LogWarning($"[Player Damage] Missing Resources/{HealthProfileResource}.asset.");
            return volume;
        }

        private static void ConfigureCameraPostProcessing(Camera camera, Volume damageVolume)
        {
            if (camera == null)
            {
                Debug.LogWarning("[Player Damage] No player camera was found; damage Volume cannot render.");
                return;
            }
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            if (damageVolume != null)
                cameraData.volumeLayerMask |= 1 << damageVolume.gameObject.layer;
        }

        private static Camera EnsureSpectatorCamera(Transform player, Camera source, Volume damageVolume)
        {
            Transform existing = player.Find("FallSpectator Camera");
            GameObject cameraObject = existing != null
                ? existing.gameObject
                : new GameObject("FallSpectator Camera");
            cameraObject.transform.SetParent(player, false);

            Camera spectator = cameraObject.GetComponent<Camera>() ?? cameraObject.AddComponent<Camera>();
            if (source != null)
                spectator.CopyFrom(source);
            spectator.enabled = false;

            AudioListener listener = cameraObject.GetComponent<AudioListener>() ??
                                     cameraObject.AddComponent<AudioListener>();
            listener.enabled = false;

            UniversalAdditionalCameraData spectatorData = spectator.GetUniversalAdditionalCameraData();
            spectatorData.renderPostProcessing = true;
            if (source != null)
            {
                UniversalAdditionalCameraData sourceData = source.GetUniversalAdditionalCameraData();
                spectatorData.volumeLayerMask = sourceData.volumeLayerMask;
            }
            if (damageVolume != null)
                spectatorData.volumeLayerMask |= 1 << damageVolume.gameObject.layer;
            return spectator;
        }
    }
}
