using System.Collections.Generic;
using UnityEngine;

public class AvatarEngine : MonoBehaviour
{
    [Header("Tracking References")]
    [SerializeField] private Transform userCamera; // Drag your XR Origin Main Camera here
    
    [Header("Pacing Settings")]
    [SerializeField] private float forwardDistance = 3.0f; // Maintains 3.0m separation
    [SerializeField] private float sampleDuration = 1.5f;   // 1.5 seconds moving average
    [SerializeField] private float targetFrameRate = 60f;  // Target execution frequency
    
    private Queue<Vector3> _positionHistory = new Queue<Vector3>();
    private int _maxSamples;

    private void Start()
    {
        // Calculate how many frames of data to hold (e.g., 60fps * 1.5s = 90 samples)
        _maxSamples = Mathf.RoundToInt(targetFrameRate * sampleDuration);
        
        if (userCamera == null)
        {
            Debug.LogError("AvatarEngine: Please assign the Main Camera from XR Origin!");
        }
    }

    private void LateUpdate()
    {
        if (userCamera == null) return;

        // 1. Calculate the raw horizontal tracking position 3.0m forward
        Vector3 forwardVector = userCamera.forward;
        forwardVector.y = 0; // Flatten the vector to prevent the avatar from flying up hills
        
        Vector3 rawTargetPosition = userCamera.position + (forwardVector.normalized * forwardDistance);

        // 2. Feed the position into the Moving Average queue to prevent sway-induced motion sickness
        _positionHistory.Enqueue(rawTargetPosition);
        if (_positionHistory.Count > _maxSamples)
        {
            _positionHistory.Dequeue();
        }

        // 3. Average out the stored coordinates
        Vector3 accumulatedPositions = Vector3.zero;
        foreach (Vector3 pos in _positionHistory)
        {
            accumulatedPositions += pos;
        }
        Vector3 smoothedPosition = accumulatedPositions / _positionHistory.Count;

        // 4. Update the horizontal position (Keep Y independent for the GroundSnap script)
        transform.position = new Vector3(smoothedPosition.x, transform.position.y, smoothedPosition.z);
    }
}