//
//  Outline.cs
//  QuickOutline
//
//  Created by Chris Nolet on 3/30/18.
//  Copyright © 2018 Chris Nolet. All rights reserved.
//

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]

public class Outline : MonoBehaviour {
  private static HashSet<Mesh> registeredMeshes = new HashSet<Mesh>();

  public enum Mode {
    OutlineAll,
    OutlineVisible,
    OutlineHidden,
    OutlineAndSilhouette,
    SilhouetteOnly
  }

  public Mode OutlineMode {
    get { return outlineMode; }
    set {
      outlineMode = value;
      needsUpdate = true;
    }
  }

  public Color OutlineColor {
    get { return outlineColor; }
    set {
      outlineColor = value;
      needsUpdate = true;
    }
  }

  public float OutlineWidth {
    get { return outlineWidth; }
    set {
      outlineWidth = value;
      needsUpdate = true;
    }
  }

  [Serializable]
  private class ListVector3 {
    public List<Vector3> data;
  }

  [SerializeField]
  private Mode outlineMode;

  [SerializeField]
  private Color outlineColor = Color.white;

  [SerializeField, Range(0f, 10f)]
  private float outlineWidth = 2f;

  [Header("Optional")]

  [SerializeField, Tooltip("Precompute enabled: Per-vertex calculations are performed in the editor and serialized with the object. "
  + "Precompute disabled: Per-vertex calculations are performed at runtime in Awake(). This may cause a pause for large meshes.")]
  private bool precomputeOutline;

  [SerializeField, HideInInspector]
  private List<Mesh> bakeKeys = new List<Mesh>();

  [SerializeField, HideInInspector]
  private List<ListVector3> bakeValues = new List<ListVector3>();

  private Renderer[] renderers;
  private Material outlineMaskMaterial;
  private Material outlineFillMaterial;


    private MaterialPropertyBlock mpb;
    private static readonly int ID_OutlineColor = Shader.PropertyToID("_OutlineColor");
    private static readonly int ID_OutlineWidth = Shader.PropertyToID("_OutlineWidth");
    private static readonly int ID_ZTest = Shader.PropertyToID("_ZTest");

    private bool needsUpdate;

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>();

        var maskAsset = Resources.Load<Material>(@"Materials/OutlineMask");
        var fillAsset = Resources.Load<Material>(@"Materials/OutlineFill");

        outlineMaskMaterial = new Material(maskAsset);
        outlineFillMaterial = new Material(fillAsset);

        // quan trọng: tránh bị lưu/động chạm keyword state trong editor
        outlineMaskMaterial.hideFlags = HideFlags.HideAndDontSave;
        outlineFillMaterial.hideFlags = HideFlags.HideAndDontSave;

        outlineMaskMaterial.name = "OutlineMask (Instance)";
        outlineFillMaterial.name = "OutlineFill (Instance)";


        mpb = new MaterialPropertyBlock();

        LoadSmoothNormals();
        needsUpdate = true;
    }

    void OnEnable() {
    foreach (var renderer in renderers) {

      // Append outline shaders
      var materials = renderer.sharedMaterials.ToList();

      materials.Add(outlineMaskMaterial);
      materials.Add(outlineFillMaterial);

            renderer.sharedMaterials = materials.ToArray();
        }
  }

  void OnValidate() {

    // Update material properties
    needsUpdate = true;

    // Clear cache when baking is disabled or corrupted
    if (!precomputeOutline && bakeKeys.Count != 0 || bakeKeys.Count != bakeValues.Count) {
      bakeKeys.Clear();
      bakeValues.Clear();
    }

    // Generate smooth normals when baking is enabled
    if (precomputeOutline && bakeKeys.Count == 0) {
      Bake();
    }
  }

  void Update() {
    if (needsUpdate) {
      needsUpdate = false;

      UpdateMaterialProperties();
    }
  }

  void OnDisable() {
    foreach (var renderer in renderers) {

      // Remove outline shaders
      var materials = renderer.sharedMaterials.ToList();

      materials.Remove(outlineMaskMaterial);
      materials.Remove(outlineFillMaterial);

            renderer.sharedMaterials = materials.ToArray();
        }
  }

  void OnDestroy() {

    // Destroy material instances
    Destroy(outlineMaskMaterial);
    Destroy(outlineFillMaterial);
  }

  void Bake() {

    // Generate smooth normals for each mesh
    var bakedMeshes = new HashSet<Mesh>();

    foreach (var meshFilter in GetComponentsInChildren<MeshFilter>()) {

      // Skip duplicates
      if (!bakedMeshes.Add(meshFilter.sharedMesh)) {
        continue;
      }

      // Serialize smooth normals
      var smoothNormals = SmoothNormals(meshFilter.sharedMesh);

      bakeKeys.Add(meshFilter.sharedMesh);
      bakeValues.Add(new ListVector3() { data = smoothNormals });
    }
  }

  void LoadSmoothNormals() {

    // Retrieve or generate smooth normals
    foreach (var meshFilter in GetComponentsInChildren<MeshFilter>()) {

      // Skip if smooth normals have already been adopted
      if (!registeredMeshes.Add(meshFilter.sharedMesh)) {
        continue;
      }

      // Retrieve or generate smooth normals
      var index = bakeKeys.IndexOf(meshFilter.sharedMesh);
      var smoothNormals = (index >= 0) ? bakeValues[index].data : SmoothNormals(meshFilter.sharedMesh);

      // Store smooth normals in UV3
      meshFilter.sharedMesh.SetUVs(3, smoothNormals);

      // Combine submeshes
      var renderer = meshFilter.GetComponent<Renderer>();

      if (renderer != null) {
        CombineSubmeshes(meshFilter.sharedMesh, renderer.sharedMaterials);
      }
    }

    // Clear UV3 on skinned mesh renderers
    foreach (var skinnedMeshRenderer in GetComponentsInChildren<SkinnedMeshRenderer>()) {

      // Skip if UV3 has already been reset
      if (!registeredMeshes.Add(skinnedMeshRenderer.sharedMesh)) {
        continue;
      }

      // Clear UV3
      skinnedMeshRenderer.sharedMesh.uv4 = new Vector2[skinnedMeshRenderer.sharedMesh.vertexCount];

      // Combine submeshes
      CombineSubmeshes(skinnedMeshRenderer.sharedMesh, skinnedMeshRenderer.sharedMaterials);
    }
  }

  List<Vector3> SmoothNormals(Mesh mesh) {

    // Group vertices by location
    var groups = mesh.vertices.Select((vertex, index) => new KeyValuePair<Vector3, int>(vertex, index)).GroupBy(pair => pair.Key);

    // Copy normals to a new list
    var smoothNormals = new List<Vector3>(mesh.normals);

    // Average normals for grouped vertices
    foreach (var group in groups) {

      // Skip single vertices
      if (group.Count() == 1) {
        continue;
      }

      // Calculate the average normal
      var smoothNormal = Vector3.zero;

      foreach (var pair in group) {
        smoothNormal += smoothNormals[pair.Value];
      }

      smoothNormal.Normalize();

      // Assign smooth normal to each vertex
      foreach (var pair in group) {
        smoothNormals[pair.Value] = smoothNormal;
      }
    }

    return smoothNormals;
  }

  void CombineSubmeshes(Mesh mesh, Material[] materials) {

    // Skip meshes with a single submesh
    if (mesh.subMeshCount == 1) {
      return;
    }

    // Skip if submesh count exceeds material count
    if (mesh.subMeshCount > materials.Length) {
      return;
    }

    // Append combined submesh
    mesh.subMeshCount++;
    mesh.SetTriangles(mesh.triangles, mesh.subMeshCount - 1);
  }

    void UpdateMaterialProperties()
    {

        // tính giá trị theo mode (giống y logic cũ)
        float maskZTest, fillZTest, width;

        switch (outlineMode)
        {
            case Mode.OutlineAll:
                maskZTest = (float)UnityEngine.Rendering.CompareFunction.Always;
                fillZTest = (float)UnityEngine.Rendering.CompareFunction.Always;
                width = outlineWidth;
                break;

            case Mode.OutlineVisible:
                maskZTest = (float)UnityEngine.Rendering.CompareFunction.Always;
                fillZTest = (float)UnityEngine.Rendering.CompareFunction.LessEqual;
                width = outlineWidth;
                break;

            case Mode.OutlineHidden:
                maskZTest = (float)UnityEngine.Rendering.CompareFunction.Always;
                fillZTest = (float)UnityEngine.Rendering.CompareFunction.Greater;
                width = outlineWidth;
                break;

            case Mode.OutlineAndSilhouette:
                maskZTest = (float)UnityEngine.Rendering.CompareFunction.LessEqual;
                fillZTest = (float)UnityEngine.Rendering.CompareFunction.Always;
                width = outlineWidth;
                break;

            case Mode.SilhouetteOnly:
                maskZTest = (float)UnityEngine.Rendering.CompareFunction.LessEqual;
                fillZTest = (float)UnityEngine.Rendering.CompareFunction.Greater;
                width = 0f;
                break;

            default:
                maskZTest = (float)UnityEngine.Rendering.CompareFunction.Always;
                fillZTest = (float)UnityEngine.Rendering.CompareFunction.LessEqual;
                width = outlineWidth;
                break;
        }

        // set property override trên renderer (không mutate material)
        mpb.Clear();
        mpb.SetColor(ID_OutlineColor, outlineColor);
        mpb.SetFloat(ID_OutlineWidth, width);

        // Trick: mpb chỉ có 1 _ZTest, nhưng shader Mask và Fill đều dùng _ZTest
        // => ta set _ZTest = fillZTest, và đổi Mask shader để đọc _ZTestMask (nếu muốn tách)
        // Nếu bạn không muốn sửa shader: set _ZTest = fillZTest và để Mask luôn Always/LessEqual trong shader.
        mpb.SetFloat(ID_ZTest, fillZTest);

        foreach (var r in renderers)
        {
            r.SetPropertyBlock(mpb);
        }

        // Nếu bạn cần MaskZTest khác FillZTest (như logic gốc),
        // thì làm “đúng bài” là sửa shader Mask dùng property _ZTestMask riêng.
    }

}
