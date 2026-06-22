using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Attach to any GameObject in Rob11Scene (e.g. the Camera).
/// Creates a global Volume with Depth of Field to blur the background
/// while keeping the robot sharp.
///
/// Assign the VolumeProfile in the Inspector so you can tweak DoF settings visually.
/// If left empty, it creates one at runtime.
/// </summary>
public class BackgroundBlurFeature : MonoBehaviour
{
    [Header("Volume Profile (optional — create via Assets > Create > Volume Profile)")]
    public VolumeProfile volumeProfile;

    [Header("Depth of Field Settings (used if no profile assigned)")]
    public float focusDistance = 1.47f;
    [Range(1f, 300f)] public float focalLength = 101f;
    [Range(0.5f, 32f)] public float aperture = 1.4f;

    private Volume _volume;
    private bool _createdProfile;

    void Start()
    {
        // If a Volume is already configured on this object (e.g. set up in the
        // scene), don't add a duplicate at runtime — just make sure post FX is on.
        var existing = GetComponent<Volume>();
        if (existing != null)
        {
            var camDataExisting = GetComponent<UniversalAdditionalCameraData>();
            if (camDataExisting != null)
                camDataExisting.renderPostProcessing = true;
            return;
        }

        _volume = gameObject.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = 100;

        if (volumeProfile != null)
        {
            _volume.profile = volumeProfile;
        }
        else
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _volume.profile = profile;
            _createdProfile = true;

            var dof = profile.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.focusDistance.Override(focusDistance);
            dof.focalLength.Override(focalLength);
            dof.aperture.Override(aperture);
        }

        var camData = GetComponent<UniversalAdditionalCameraData>();
        if (camData != null)
            camData.renderPostProcessing = true;
    }

    void OnDestroy()
    {
        if (_createdProfile && _volume != null && _volume.profile != null)
            DestroyImmediate(_volume.profile);
    }
}
