using System;
using UnityEngine;

[Serializable]
public class Triangles : MonoBehaviour
{
    public static Mesh[] meshes;
    public static int currentTris;

    public static bool HasMeshes()
    {
        if (meshes == null)
        {
            return false;
        }
        foreach (var m in meshes)
        {
            if (null == m)
            {
                return false;
            }
        }
        return true;
    }

    public static void Cleanup()
    {
        if (meshes == null)
        {
            return;
        }
        foreach (var m in meshes)
        {
            if (null != m)
            {
                DestroyImmediate(m);
            }
        }
        meshes = null;
    }

    public static Mesh[] GetMeshes(int totalWidth, int totalHeight)
    {
        if (HasMeshes() && (currentTris == (totalWidth * totalHeight)))
        {
            return meshes;
        }
        const int maxTris = 65000 / 3;
        var totalTris = totalWidth * totalHeight;
        currentTris = totalTris;
        var meshCount = Mathf.CeilToInt((1f * totalTris) / (1f * maxTris));
        meshes = new Mesh[meshCount];
        var i = 0;
        var index = 0;
        i = 0;
        while (i < totalTris)
        {
            var tris = Mathf.FloorToInt(Mathf.Clamp(totalTris - i, 0, maxTris));
            meshes[index] = GetMesh(tris, i, totalWidth, totalHeight);
            index++;
            i = i + maxTris;
        }
        return meshes;
    }

    public static Mesh GetMesh(int triCount, int triOffset, int totalWidth, int totalHeight)
    {
        var mesh = new Mesh();
        mesh.hideFlags = HideFlags.DontSave;
        var verts = new Vector3[triCount * 3];
        var uvs = new Vector2[triCount * 3];
        var uvs2 = new Vector2[triCount * 3];
        var tris = new int[triCount * 3];
        var i = 0;
        while (i < triCount)
        {
            var i3 = i * 3;
            var vertexWithOffset = triOffset + i;
            var x = Mathf.Floor(vertexWithOffset % totalWidth) / totalWidth;
            var y = Mathf.Floor(vertexWithOffset / totalWidth) / totalHeight;
            var position = new Vector3((x * 2) - 1, (y * 2) - 1, 1f);
            verts[i3 + 0] = position;
            verts[i3 + 1] = position;
            verts[i3 + 2] = position;
            uvs[i3 + 0] = new Vector2(0f, 0f);
            uvs[i3 + 1] = new Vector2(1f, 0f);
            uvs[i3 + 2] = new Vector2(0f, 1f);
            uvs2[i3 + 0] = new Vector2(x, y);
            uvs2[i3 + 1] = new Vector2(x, y);
            uvs2[i3 + 2] = new Vector2(x, y);
            tris[i3 + 0] = i3 + 0;
            tris[i3 + 1] = i3 + 1;
            tris[i3 + 2] = i3 + 2;
            i++;
        }
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.uv = uvs;
        mesh.uv2 = uvs2;
        return mesh;
    }
}
