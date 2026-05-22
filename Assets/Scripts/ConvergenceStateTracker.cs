using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class ConvergenceStateTracker
{
    // Snapshot of every per-frame value whose change invalidates the accumulated image. It is
    // compared by value each frame, so a single difference means accumulation must restart.
    private struct Snapshot : System.IEquatable<Snapshot>
    {
        public Matrix4x4 cameraToWorld;
        public uint bounceCountOpaque;
        public uint bounceCountTransparent;
        public bool debugSingleBounce;
        public uint debugBounceIndex;
        public int lightHash;
        public uint instanceCount;

        public bool Equals(Snapshot other)
        {
            return cameraToWorld == other.cameraToWorld
                && bounceCountOpaque == other.bounceCountOpaque
                && bounceCountTransparent == other.bounceCountTransparent
                && debugSingleBounce == other.debugSingleBounce
                && debugBounceIndex == other.debugBounceIndex
                && lightHash == other.lightHash
                && instanceCount == other.instanceCount;
        }
    }

    // RayTracingInstanceMaterialCRC.instanceID became EntityId entityId in Unity 6.4, so the key
    // type follows the running version. The two maps are double buffered: each frame the live
    // materials are written into one and compared against the other. Materials that left the scene
    // are absent from the new baseline, so neither map grows without bound.
#if UNITY_6000_4_OR_NEWER
    private Dictionary<EntityId, int> prevMaterialCRCs = new Dictionary<EntityId, int>();
    private Dictionary<EntityId, int> currMaterialCRCs = new Dictionary<EntityId, int>();
#else
    private Dictionary<int, int> prevMaterialCRCs = new Dictionary<int, int>();
    private Dictionary<int, int> currMaterialCRCs = new Dictionary<int, int>();
#endif

    private Snapshot prevSnapshot;
    private int step = 0;

    // The current progressive averaging step, uploaded to the ray gen shader as g_ConvergenceStep.
    public int Step { get { return step; } }

    public void Reset()
    {
        step = 0;
    }

    public void Advance()
    {
        step++;
    }

    public void DetectInvalidation(Camera camera, uint bounceCountOpaque, uint bounceCountTransparent, bool debugSingleBounce, uint debugBounceIndex, int lightHash, uint instanceCount, RayTracingInstanceCullingResults cullingResult)
    {
        Snapshot current = new Snapshot
        {
            cameraToWorld          = camera.cameraToWorldMatrix,
            bounceCountOpaque      = bounceCountOpaque,
            bounceCountTransparent = bounceCountTransparent,
            debugSingleBounce      = debugSingleBounce,
            debugBounceIndex       = debugSingleBounce ? debugBounceIndex : 0u,
            lightHash              = lightHash,
            instanceCount          = instanceCount,
        };

        bool reset = !current.Equals(prevSnapshot);
        reset |= UpdateMaterialCRCs(cullingResult);
        reset |= cullingResult.transformsChanged;

        prevSnapshot = current;

        if (reset)
            Reset();
    }

    private bool UpdateMaterialCRCs(RayTracingInstanceCullingResults cullingResult)
    {
        RayTracingInstanceMaterialCRC[] materials = cullingResult.materialsCRC;
        if (materials == null)
            return false;

        currMaterialCRCs.Clear();

        bool materialsDirty = false;
        foreach (RayTracingInstanceMaterialCRC material in materials)
        {
#if UNITY_6000_4_OR_NEWER
            EntityId id = material.entityId;
#else
            int id = material.instanceID;
#endif
            int prevCRC;
            if (!prevMaterialCRCs.TryGetValue(id, out prevCRC) || prevCRC != material.crc)
                materialsDirty = true;

            currMaterialCRCs[id] = material.crc;
        }

        // This frame's map becomes next frame's baseline; the old baseline is reused as scratch,
        // cleared at the top of the next call. Materials no longer present are dropped here.
        (prevMaterialCRCs, currMaterialCRCs) = (currMaterialCRCs, prevMaterialCRCs);

        return materialsDirty;
    }
}
