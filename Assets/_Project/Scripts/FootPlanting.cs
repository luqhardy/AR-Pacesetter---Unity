using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 足のめり込み補正 (基本設計書 §10: 接地誤差 上下5cm以内)。
///
/// <para><b>何が壊れていたか</b>: <see cref="GroundSnap"/> が床へ合わせるのはアバターの<b>原点</b>だが、
/// Mixamoの走行クリップは立脚期に足を原点の平面より下へ下ろす。そのため床の推定が完璧でも
/// 足裏は約6cm(スキンメッシュの最下頂点では最大約10cm)床へめり込んでいた。</para>
///
/// <para><b>やり方</b>: モデル読込時に、足/つま先の骨に属する頂点のうち安静姿勢で足裏の面にあるもの
/// (最下点・前後端・左右端)を骨ローカル座標で覚える。毎フレーム、アニメーション適用後に
/// その点の最下点を測り、原点より下ならモデルをその分だけ持ち上げる
/// (<see cref="GroundContactMath.ComputeLift"/>)。上げるだけで下げないので走行の空中局面は残る。</para>
///
/// <para>原点(=ルート)の高さは <see cref="GroundSnap"/> のまま触らない。動かすのはモデル
/// (Animatorの付いた子)の localPosition だけなので、床の推定・断崖判定・影とは独立している。</para>
///
/// <para>メッシュが読めない(Read/Write無効)モデルでは足首/つま先の骨から概算する。</para>
/// </summary>
[DefaultExecutionOrder(10000)] // Animator・ジェスチャー(LateUpdate)の後に測る
public class FootPlanting : MonoBehaviour
{
    [Tooltip("無効にするとモデルを元の位置へ戻す")]
    [SerializeField] private bool planting = true;

    /// <summary>直近フレームで持ち上げた量(ルートのローカル単位、ほぼm)。</summary>
    public float CurrentLiftMeters { get; private set; }

    /// <summary>メッシュから足裏の点を取れたか(false なら骨からの概算)。</summary>
    public bool UsesMeshSolePoints { get; private set; }

    /// <summary>覚えている足裏の点の数。</summary>
    public int SolePointCount => _solePoints.Count;

    // 骨の概算(メッシュが読めない場合)。成人男性の実寸
    private const float AnkleToSoleMeters = 0.09f;
    private const float ToesToSoleMeters = 0.025f;
    private const float ReResolveIntervalSeconds = 0.5f;

    private struct SolePoint
    {
        public Transform Bone;
        public Vector3 Local;
        public SolePoint(Transform bone, Vector3 local) { Bone = bone; Local = local; }
    }

    private readonly List<SolePoint> _solePoints = new List<SolePoint>();
    private Animator _animator;
    private Transform _model;
    private Vector3 _modelBaseLocalPosition;
    private float _nextResolveTime;

    public bool Planting
    {
        get => planting;
        set { planting = value; if (!value) ResetLift(); }
    }

    private void LateUpdate()
    {
        if (!planting) return;
        if (!EnsureModel()) { CurrentLiftMeters = 0f; return; }

        float lowest = MeasureLowestSoleAboveRoot();
        float raw = lowest - CurrentLiftMeters;   // 今掛かっている持ち上げを除いた素の高さ
        ApplyLift(GroundContactMath.ComputeLift(raw));
    }

    private void OnDisable() => ResetLift();

    /// <summary>
    /// モデルの足裏の最下点の、ルートからの高さ(ルートのローカル単位)。補正込みの現在値。
    /// </summary>
    public float MeasureLowestSoleAboveRoot()
    {
        float lowest = float.PositiveInfinity;
        if (_solePoints.Count > 0)
        {
            foreach (SolePoint p in _solePoints)
            {
                if (p.Bone == null) continue;
                lowest = Mathf.Min(lowest, transform.InverseTransformPoint(p.Bone.TransformPoint(p.Local)).y);
            }
        }
        else if (_animator != null && _animator.isHuman)
        {
            lowest = Mathf.Min(lowest, BoneEstimate(HumanBodyBones.LeftFoot, AnkleToSoleMeters));
            lowest = Mathf.Min(lowest, BoneEstimate(HumanBodyBones.RightFoot, AnkleToSoleMeters));
            lowest = Mathf.Min(lowest, BoneEstimate(HumanBodyBones.LeftToes, ToesToSoleMeters));
            lowest = Mathf.Min(lowest, BoneEstimate(HumanBodyBones.RightToes, ToesToSoleMeters));
        }
        return float.IsPositiveInfinity(lowest) ? float.NaN : lowest;
    }

    private float BoneEstimate(HumanBodyBones bone, float toSole)
    {
        Transform t = _animator.GetBoneTransform(bone);
        return t == null ? float.PositiveInfinity : transform.InverseTransformPoint(t.position).y - toSole;
    }

    // ── モデルの解決(差し替えに追従) ─────────────────────────────────────

    private bool EnsureModel()
    {
        bool stale = _animator == null || !_animator.isActiveAndEnabled;
        if (stale || Time.unscaledTime >= _nextResolveTime)
        {
            _nextResolveTime = Time.unscaledTime + ReResolveIntervalSeconds;
            Animator best = AvatarRigLocator.FindBestAnimator(transform);
            if (best != null && (!best.isActiveAndEnabled || !best.isHuman)) best = null;
            if (best != _animator) Rebind(best);
        }
        return _model != null;
    }

    private void Rebind(Animator animator)
    {
        ResetLift();
        _animator = animator;
        _model = animator != null && animator.transform != transform ? animator.transform : null;
        _solePoints.Clear();
        UsesMeshSolePoints = false;
        if (_model == null) return;

        _modelBaseLocalPosition = _model.localPosition;
        CalibrateFromMesh();
        Debug.Log($"[FootPlanting] {_model.name}: " +
                  (UsesMeshSolePoints ? $"{_solePoints.Count} sole points from the mesh" : "bone estimate (mesh not readable)"));
    }

    /// <summary>
    /// 安静姿勢(バインドポーズ)のメッシュから、足/つま先の骨に属する足裏の点を選ぶ。
    /// バインドポーズで測るので、呼んだ瞬間のアニメーションの姿勢には左右されない。
    /// </summary>
    private void CalibrateFromMesh()
    {
        var targets = new HashSet<Transform>();
        foreach (HumanBodyBones b in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
                                             HumanBodyBones.LeftToes, HumanBodyBones.RightToes })
        {
            Transform t = _animator.GetBoneTransform(b);
            if (t != null) targets.Add(t);
        }
        if (targets.Count == 0) return;

        Vector3 up = _model.up, fwd = _model.forward, right = _model.right;

        foreach (SkinnedMeshRenderer smr in _model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Mesh mesh = smr.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;

            Transform[] bones = smr.bones;
            Matrix4x4[] bindposes = mesh.bindposes;
            BoneWeight[] weights = mesh.boneWeights;
            Vector3[] vertices = mesh.vertices;
            if (weights.Length != vertices.Length) continue;

            // 骨ごとに頂点を集める(最も重みの大きい骨 = boneIndex0 に帰属させる)
            var perBone = new Dictionary<int, List<int>>();
            for (int v = 0; v < vertices.Length; v++)
            {
                int bi = weights[v].boneIndex0;
                if (bi < 0 || bi >= bones.Length || bi >= bindposes.Length) continue;
                if (!targets.Contains(bones[bi])) continue;
                if (!perBone.TryGetValue(bi, out var list)) perBone[bi] = list = new List<int>();
                list.Add(v);
            }

            Matrix4x4 meshToWorld = smr.transform.localToWorldMatrix;
            foreach (var kv in perBone)
            {
                var heights = new List<float>(kv.Value.Count);
                var forwards = new List<float>(kv.Value.Count);
                var sides = new List<float>(kv.Value.Count);
                foreach (int v in kv.Value)
                {
                    Vector3 rest = meshToWorld.MultiplyPoint3x4(vertices[v]);
                    heights.Add(Vector3.Dot(rest, up));
                    forwards.Add(Vector3.Dot(rest, fwd));
                    sides.Add(Vector3.Dot(rest, right));
                }

                // 足裏の面の厚みはワールド単位で決める(モデルの縮尺で変わらないように)
                foreach (int pick in GroundContactMath.SelectSolePoints(
                             heights, forwards, sides, GroundContactMath.SoleBandMeters))
                {
                    int v = kv.Value[pick];
                    _solePoints.Add(new SolePoint(bones[kv.Key], bindposes[kv.Key].MultiplyPoint3x4(vertices[v])));
                }
            }
        }
        UsesMeshSolePoints = _solePoints.Count > 0;
    }

    // ── 適用 ─────────────────────────────────────────────────────────────

    private void ApplyLift(float lift)
    {
        CurrentLiftMeters = lift;
        if (_model == null) return;

        Vector3 offset = Vector3.up * lift;   // ルートのローカル
        if (_model.parent != transform && _model.parent != null)
            offset = _model.parent.InverseTransformVector(transform.TransformVector(offset));
        _model.localPosition = _modelBaseLocalPosition + offset;
    }

    private void ResetLift()
    {
        if (_model != null) _model.localPosition = _modelBaseLocalPosition;
        CurrentLiftMeters = 0f;
    }
}
