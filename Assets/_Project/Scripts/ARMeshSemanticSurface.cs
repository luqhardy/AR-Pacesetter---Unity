using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// ARMeshManager が生成した1メッシュの面分類を保持する。
/// MeshCollider の RaycastHit.triangleIndex と同じ面順で参照できる。
/// </summary>
public sealed class ARMeshSemanticSurface : MonoBehaviour
{
    private uint[] _faceClassifications = System.Array.Empty<uint>();

    public int ClassifiedFaceCount { get; private set; }
    public int FaceCount => _faceClassifications.Length;

    public void SetClassifications(NativeArray<uint> classifications)
    {
        if (!classifications.IsCreated || classifications.Length == 0)
        {
            ClearClassifications();
            return;
        }

        // ARKitは同じメッシュ片を繰り返し更新する。面数が同じ間は配列を再利用し、
        // 更新ごとのGCを避けて60fps経路のジッタを増やさない。
        if (_faceClassifications.Length != classifications.Length)
            _faceClassifications = new uint[classifications.Length];
        classifications.CopyTo(_faceClassifications);

        int known = 0;
        for (int i = 0; i < _faceClassifications.Length; i++)
        {
            if ((XRMeshClassification)_faceClassifications[i] != XRMeshClassification.Unknown)
                known++;
        }
        ClassifiedFaceCount = known;
    }

    public void ClearClassifications()
    {
        _faceClassifications = System.Array.Empty<uint>();
        ClassifiedFaceCount = 0;
    }

    public bool TryGetSemantic(int triangleIndex, out SurfaceSemantic semantic)
    {
        semantic = SurfaceSemantic.Unknown;
        if (triangleIndex < 0 || triangleIndex >= _faceClassifications.Length)
            return false;

        semantic = FromMeshClassification((XRMeshClassification)_faceClassifications[triangleIndex]);
        return true;
    }

    /// <summary>
    /// 3D面分類に、屋外の画像分類(ARCore Scene Semantics)を重ねて解決する。
    ///
    /// <para>3D側が明示的に分類していればそちらが勝つ(<see cref="SurfaceSemanticMath.Combine"/>)。
    /// 画像側が効くのは、ARKitの語彙に road が無いせいで屋外路面が Unknown で
    /// 落ちてくるときだけ。供給元が無い/未対応なら結果は従来と完全に同一。</para>
    /// </summary>
    public static SurfaceSemantic FromRaycastHit(RaycastHit hit, IOutdoorSemanticSource outdoor)
    {
        SurfaceSemantic geometric = FromRaycastHit(hit);

        if (outdoor == null || !outdoor.IsAvailable) return geometric;
        if (!outdoor.TryClassify(hit.point, out SurfaceSemantic image)) return geometric;

        return SurfaceSemanticMath.Combine(geometric, image);
    }

    /// <summary>ARPlane の分類に屋外の画像分類を重ねる(平面ヒットにはRaycastHitが無いため点で問い合わせる)。</summary>
    public static SurfaceSemantic FromPlaneClassifications(PlaneClassifications classifications,
                                                           Vector3 worldPoint,
                                                           IOutdoorSemanticSource outdoor)
    {
        SurfaceSemantic geometric = FromPlaneClassifications(classifications);

        if (outdoor == null || !outdoor.IsAvailable) return geometric;
        if (!outdoor.TryClassify(worldPoint, out SurfaceSemantic image)) return geometric;

        return SurfaceSemanticMath.Combine(geometric, image);
    }

    public static SurfaceSemantic FromRaycastHit(RaycastHit hit)
    {
        if (hit.collider == null)
            return SurfaceSemantic.Unknown;

        var meshSurface = hit.collider.GetComponent<ARMeshSemanticSurface>();
        if (meshSurface == null)
            meshSurface = hit.collider.GetComponentInParent<ARMeshSemanticSurface>();
        if (meshSurface != null && meshSurface.TryGetSemantic(hit.triangleIndex, out SurfaceSemantic meshSemantic))
            return meshSemantic;

        var plane = hit.collider.GetComponent<ARPlane>();
        if (plane == null)
            plane = hit.collider.GetComponentInParent<ARPlane>();
        return plane != null ? FromPlaneClassifications(plane.classifications) : SurfaceSemantic.Unknown;
    }

    public static SurfaceSemantic FromPlaneClassifications(PlaneClassifications classifications)
    {
        if ((classifications & PlaneClassifications.Floor) != 0) return SurfaceSemantic.Floor;
        if ((classifications & PlaneClassifications.Ceiling) != 0) return SurfaceSemantic.Ceiling;
        if ((classifications & PlaneClassifications.Table) != 0) return SurfaceSemantic.Table;
        if ((classifications & PlaneClassifications.SeatOfAnyType) != 0) return SurfaceSemantic.Seat;
        if ((classifications & (PlaneClassifications.WallFace |
                                PlaneClassifications.InnerWallFace |
                                PlaneClassifications.InvisibleWallFace |
                                PlaneClassifications.WallArt)) != 0) return SurfaceSemantic.Wall;
        if ((classifications & PlaneClassifications.DoorFrame) != 0) return SurfaceSemantic.Door;
        if ((classifications & PlaneClassifications.WindowFrame) != 0) return SurfaceSemantic.Window;
        if ((classifications & PlaneClassifications.Other) != 0) return SurfaceSemantic.Other;
        return SurfaceSemantic.Unknown;
    }

    private static SurfaceSemantic FromMeshClassification(XRMeshClassification classification)
    {
        switch (classification)
        {
            case XRMeshClassification.Floor: return SurfaceSemantic.Floor;
            case XRMeshClassification.Ceiling: return SurfaceSemantic.Ceiling;
            case XRMeshClassification.Wall: return SurfaceSemantic.Wall;
            case XRMeshClassification.Table: return SurfaceSemantic.Table;
            case XRMeshClassification.Seat: return SurfaceSemantic.Seat;
            case XRMeshClassification.Window: return SurfaceSemantic.Window;
            case XRMeshClassification.Door: return SurfaceSemantic.Door;
            case XRMeshClassification.Other: return SurfaceSemantic.Other;
            default: return SurfaceSemantic.Unknown;
        }
    }
}
