using UnityEngine;

namespace Terrainity
{
    // Unity's WindZone data is not sent to custom tree/grass shaders automatically.
    // One shared driver supplies the strongest directional zone and up to four local zones.
    [DefaultExecutionOrder(-100)]
    public sealed class TreeWindController : MonoBehaviour
    {
        const int MaxSpheres = 4;
        static readonly int DirectionId = Shader.PropertyToID("_TerrainityWindDirection");
        static readonly int MotionId = Shader.PropertyToID("_TerrainityWindMotion");
        static readonly int SphereCountId = Shader.PropertyToID("_TerrainityWindSphereCount");
        static readonly int SpheresId = Shader.PropertyToID("_TerrainityWindSphere");
        static readonly int SphereMotionId = Shader.PropertyToID("_TerrainityWindSphereMotion");
        static readonly int TimeId = Shader.PropertyToID("_TerrainityWindTime");
        static TreeWindController instance;

        readonly Vector4[] spheres = new Vector4[MaxSpheres];
        readonly Vector4[] sphereMotion = new Vector4[MaxSpheres];
        WindZone[] zones = System.Array.Empty<WindZone>();
        float nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetDriver()
        {
            // Also runs when Enter Play Mode skips domain reload.
            instance = null;
            ClearWind();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartDriver()
        {
            if (instance != null) return;
            var existing = FindFirstObjectByType<TreeWindController>();
            if (existing != null && existing.isActiveAndEnabled)
            {
                instance = existing;
                existing.nextScan = 0;
                return;
            }
            var driver = new GameObject("Terrainity Wind") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(driver);
            instance = driver.AddComponent<TreeWindController>();
        }

        void Awake()
        {
            // A duplicate component may be on a user's scene object; preserve that object.
            if (instance != null && instance != this) { Destroy(this); return; }
            instance = this;
        }

        void OnEnable()
        {
            if (instance == null) instance = this;
            nextScan = 0;
        }

        void Update()
        {
            if (instance != this) return;
            if (Time.unscaledTime >= nextScan)
            {
                zones = FindObjectsByType<WindZone>(FindObjectsInactive.Exclude);
                nextScan = Time.unscaledTime + 1f;
            }
            WindZone directional = null;
            float strongest = 0;
            int count = 0;
            foreach (var zone in zones)
            {
                if (zone == null || !zone.gameObject.activeInHierarchy || zone.windMain <= 0) continue;
                if (zone.mode == WindZoneMode.Directional)
                {
                    if (zone.windMain <= strongest) continue;
                    strongest = zone.windMain;
                    directional = zone;
                }
                else if (count < MaxSpheres)
                {
                    Vector3 center = zone.transform.position;
                    spheres[count] = new Vector4(center.x, center.y, center.z, Mathf.Max(.01f, zone.radius));
                    sphereMotion[count] = new Vector4(zone.windMain, zone.windTurbulence,
                        zone.windPulseMagnitude, zone.windPulseFrequency);
                    count++;
                }
            }
            Vector3 direction = directional != null ? directional.transform.forward : Vector3.zero;
            Shader.SetGlobalVector(DirectionId, new Vector4(direction.x, direction.y, direction.z, strongest));
            Shader.SetGlobalVector(MotionId, directional != null
                ? new Vector4(directional.windTurbulence, directional.windPulseMagnitude,
                    directional.windPulseFrequency, 0) : Vector4.zero);
            Shader.SetGlobalFloat(SphereCountId, count);
            Shader.SetGlobalVectorArray(SpheresId, spheres);
            Shader.SetGlobalVectorArray(SphereMotionId, sphereMotion);
            Shader.SetGlobalFloat(TimeId, Time.time);
        }

        void OnDisable()
        {
            if (instance != this) return;
            instance = null;
            ClearWind();
        }

        void OnDestroy() => OnDisable();

        static void ClearWind()
        {
            Shader.SetGlobalVector(DirectionId, Vector4.zero);
            Shader.SetGlobalVector(MotionId, Vector4.zero);
            Shader.SetGlobalFloat(SphereCountId, 0);
            Shader.SetGlobalFloat(TimeId, 0);
        }
    }
}
