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
