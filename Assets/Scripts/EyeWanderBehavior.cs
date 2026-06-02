using System.Collections;
using UnityEngine;

/// <summary>
/// Subtle idle eye-look wander. Picks a random gaze direction every few
/// seconds and drives the ARKit eyeLookIn/Out/Up/Down blend shapes on the
/// face mesh so the character's eyes feel alive instead of locked.
///
/// Attach to the same GameObject as your character's face SkinnedMeshRenderer.
/// </summary>
public class EyeWanderBehavior : MonoBehaviour
{
    [Tooltip("Face mesh with ARKit eyeLook* blend shapes (same one EmotionController uses).")]
    public SkinnedMeshRenderer faceMesh;

    [Tooltip("Maximum eye deflection (0..1). 0.6 looks natural; 1.0 is cartoony.")]
    [Range(0.1f, 1f)] public float maxDeflection = 0.5f;

    [Tooltip("Average seconds between gaze direction changes.")]
    public float intervalAverage = 2.5f;

    [Tooltip("± random jitter on the interval.")]
    public float intervalJitter = 1.2f;

    [Tooltip("How fast the eyes move toward a new target (per second).")]
    public float lerpSpeed = 3f;

    [Tooltip("Probability the new gaze target is roughly forward (eyes return to centre). Higher = less wandery.")]
    [Range(0f, 1f)] public float centreBias = 0.35f;

    private int _idxInL, _idxOutL, _idxInR, _idxOutR;
    private int _idxUpL, _idxUpR, _idxDownL, _idxDownR;

    private Vector2 _currentGaze; // x: -1=left, +1=right; y: -1=down, +1=up
    private Vector2 _targetGaze;

    void Start()
    {
        if (faceMesh == null || faceMesh.sharedMesh == null)
        {
            enabled = false;
            return;
        }
        var mesh = faceMesh.sharedMesh;
        _idxInL    = mesh.GetBlendShapeIndex("eyeLookInLeft");
        _idxOutL   = mesh.GetBlendShapeIndex("eyeLookOutLeft");
        _idxInR    = mesh.GetBlendShapeIndex("eyeLookInRight");
        _idxOutR   = mesh.GetBlendShapeIndex("eyeLookOutRight");
        _idxUpL    = mesh.GetBlendShapeIndex("eyeLookUpLeft");
        _idxUpR    = mesh.GetBlendShapeIndex("eyeLookUpRight");
        _idxDownL  = mesh.GetBlendShapeIndex("eyeLookDownLeft");
        _idxDownR  = mesh.GetBlendShapeIndex("eyeLookDownRight");

        if (_idxInL < 0 || _idxOutL < 0)
        {
            // Mesh doesn't have ARKit eye-look shapes; bail quietly.
            enabled = false;
            return;
        }
        StartCoroutine(WanderLoop());
    }

    private IEnumerator WanderLoop()
    {
        while (enabled)
        {
            float wait = intervalAverage + Random.Range(-intervalJitter, intervalJitter);
            yield return new WaitForSeconds(Mathf.Max(0.4f, wait));

            if (Random.value < centreBias)
            {
                _targetGaze = new Vector2(Random.Range(-0.15f, 0.15f), Random.Range(-0.1f, 0.1f));
            }
            else
            {
                _targetGaze = new Vector2(
                    Random.Range(-maxDeflection, maxDeflection),
                    Random.Range(-maxDeflection * 0.7f, maxDeflection * 0.7f));
            }
        }
    }

    void Update()
    {
        if (faceMesh == null) return;
        _currentGaze = Vector2.MoveTowards(_currentGaze, _targetGaze, lerpSpeed * Time.deltaTime);

        // Convert (-1..1, -1..1) to ARKit blend shape weights (0..100).
        float right = Mathf.Max(0f, _currentGaze.x);
        float left  = Mathf.Max(0f, -_currentGaze.x);
        float up    = Mathf.Max(0f, _currentGaze.y);
        float down  = Mathf.Max(0f, -_currentGaze.y);

        // Look right = left eye looks "out", right eye looks "in"
        SetWeight(_idxOutL, right);
        SetWeight(_idxInR,  right);
        // Look left = left eye looks "in", right eye looks "out"
        SetWeight(_idxInL,  left);
        SetWeight(_idxOutR, left);
        // Up/down both eyes
        SetWeight(_idxUpL,   up);
        SetWeight(_idxUpR,   up);
        SetWeight(_idxDownL, down);
        SetWeight(_idxDownR, down);
    }

    private void SetWeight(int idx, float normalized01)
    {
        if (idx < 0) return;
        faceMesh.SetBlendShapeWeight(idx, Mathf.Clamp01(normalized01) * 100f);
    }
}
