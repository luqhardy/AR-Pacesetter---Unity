using UnityEngine;

/// <summary>
/// アバターリグの解決ヘルパー。
/// Avatar_Container には無効化された旧Animatorが残っていることがあり、
/// 単純な GetComponentInChildren&lt;Animator&gt;() はそれを拾ってしまう
/// （→ 表示中のモデルにSpeed等が届かずアバターが滑走する）。
/// ここでは「有効・アクティブ・コントローラ付き」のAnimatorを優先して返す。
/// </summary>
public static class AvatarRigLocator
{
    public static Animator FindBestAnimator(Transform root)
    {
        if (root == null) return null;

        Animator[] all = root.GetComponentsInChildren<Animator>(true);

        // 1st choice: enabled, on an active GameObject, controller assigned
        foreach (Animator anim in all)
        {
            if (anim.enabled && anim.gameObject.activeInHierarchy
                && anim.runtimeAnimatorController != null)
                return anim;
        }

        // 2nd choice: enabled and active (controller can be assigned afterwards)
        foreach (Animator anim in all)
        {
            if (anim.enabled && anim.gameObject.activeInHierarchy)
                return anim;
        }

        // Last resort: anything, so callers can at least log a meaningful state
        return all.Length > 0 ? all[0] : null;
    }
}
