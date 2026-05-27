using UnityEngine;

public class GroundSnap : MonoBehaviour
{
    [Header("Smoothing Settings")]
    [SerializeField] private float smoothTime = 0.3f;        // 0.3 seconds easing rule
    [SerializeField] private float stepThreshold = 0.15f;    // Trigger smoothing if delta > 15cm

    private float _targetY;
    private float _currentYVelocity;

    private void Start()
    {
        _targetY = transform.position.y;
    }

    private void Update()
    {
        // Simulated Ground Height checking. 
        // In your final build, you will pass the LiDAR/Raycast intersection point here.
        float currentDetectedGroundHeight = GetCurrentGroundLevel(); 

        // If the vertical delta exceeds 15cm, lock in the new target ground level
        if (Mathf.Abs(transform.position.y - currentDetectedGroundHeight) > stepThreshold)
        {
            _targetY = currentDetectedGroundHeight;
        }

        // Apply a mathematically precise Dampening equation over a 0.3s window
        float smoothedY = Mathf.SmoothDamp(transform.position.y, _targetY, ref _currentYVelocity, smoothTime);
        
        transform.position = new Vector3(transform.position.x, smoothedY, transform.position.z);
    }

    private float GetCurrentGroundLevel()
    {
        // Placeholder: For now, it locks to Unity's zero-plane. 
        // Later, this links directly to your AR Foundation ARPlaneManager or LiDAR mesh.
        return 0f; 
    }
}