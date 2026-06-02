using System.Collections.Generic;
using UnityEngine;

// Spawns obstacles ahead of the player and scrolls them toward camera.
// Player stays at z = 0; obstacles spawn at +spawnZ and travel to -despawnZ.
public class ObstacleSpawner : MonoBehaviour
{
    [Header("Prefab")]
    [Tooltip("Prefab with Collider (isTrigger=true) and tag = Obstacle.")]
    public GameObject obstaclePrefab;

    [Header("Lanes (must match RobotRunner.laneXs)")]
    public float[] laneXs = new float[] { -2f, 0f, 2f };

    [Header("Scrolling")]
    public float scrollSpeed = 8f;
    public float spawnZ = 30f;
    public float despawnZ = -10f;
    public float spawnInterval = 1.2f;

    [Header("Refs")]
    public RunnerGameManager gameManager;

    private float _timer;
    private readonly List<Transform> _live = new List<Transform>();

    void Update()
    {
        if (gameManager != null && gameManager.IsGameOver) return;

        _timer += Time.deltaTime;
        if (_timer >= spawnInterval)
        {
            _timer = 0f;
            Spawn();
        }

        // Scroll all live obstacles, despawn off the back.
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var t = _live[i];
            if (t == null) { _live.RemoveAt(i); continue; }
            var p = t.position;
            p.z -= scrollSpeed * Time.deltaTime;
            t.position = p;
            if (p.z < despawnZ)
            {
                Destroy(t.gameObject);
                _live.RemoveAt(i);
            }
        }
    }

    void Spawn()
    {
        if (obstaclePrefab == null || laneXs.Length == 0) return;
        int lane = Random.Range(0, laneXs.Length);
        var pos = new Vector3(laneXs[lane], 0.5f, spawnZ);
        var go = Instantiate(obstaclePrefab, pos, Quaternion.identity);
        _live.Add(go.transform);
    }

    public void ClearAll()
    {
        foreach (var t in _live) if (t != null) Destroy(t.gameObject);
        _live.Clear();
        _timer = 0f;
    }
}
