using UnityEngine;

/// <summary>
/// Helper component that attaches to the specific GameObject containing the Animator.
/// It routes the OnAnimatorIK Unity callback up to the parent OvertakeBehaviourController.
/// </summary>
public class AvatarIKRelay : MonoBehaviour
{
    public OvertakeBehaviourController TargetController { get; set; }

    private void OnAnimatorIK(int layerIndex)
    {
        if (TargetController != null)
        {
            TargetController.HandleAnimatorIK(layerIndex);
        }
    }
}
