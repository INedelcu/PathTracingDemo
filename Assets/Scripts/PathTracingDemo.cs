using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

[ExecuteInEditMode]
public class PathTracingDemo : MonoBehaviour
{
    public RayTracingShader rayTracingShader = null;

    public Cubemap envTexture = null;

    [Range(1, 100)]
    public uint bounceCountOpaque = 5;

    [Range(1, 100)]
    public uint bounceCountTransparent = 8;

    [Header("Debug")]
    public bool debugSingleBounce = false;

    [Range(0, 100)]
    [Tooltip("When Debug Single Bounce is enabled, this selects which bounce to visualize. 0 is the primary ray hit (directly visible emission and direct lighting).")]
    public uint debugBounceIndex = 0;

    private uint cameraWidth = 0;
    private uint cameraHeight = 0;

    private readonly ConvergenceStateTracker convergenceTracker = new ConvergenceStateTracker();

    private RenderTexture rayTracingOutput = null;

    private RayTracingAccelerationStructure rayTracingAccelerationStructure = null;

    // Layout must match the Light struct in Assets/Shaders/Lights.hlsl (48 bytes).
    [StructLayout(LayoutKind.Sequential)]
    private struct LightData
    {
        public Vector3 color;
        public uint    type;        // 0 = directional, 1 = point
        public Vector3 direction;
        public float   range;
        public Vector3 position;
        public float   _pad0;
    }

    private const int LIGHT_DATA_STRIDE = 48;

    private GraphicsBuffer lightsBuffer = null;
    private readonly List<LightData> lightsScratch = new List<LightData>();
    // Always uploaded with at least one entry so the StructuredBuffer binding is
    // never zero-sized. g_LightCount controls how many entries the shader actually iterates over.
    private static readonly LightData[] DummyLights = new LightData[1];

    private void CreateRayTracingAccelerationStructure()
    {
        if (rayTracingAccelerationStructure == null)
        {
            RayTracingAccelerationStructure.Settings settings = new RayTracingAccelerationStructure.Settings()
            {
                rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                managementMode = RayTracingAccelerationStructure.ManagementMode.Manual,
                layerMask = 255
            };
            rayTracingAccelerationStructure = new RayTracingAccelerationStructure(settings);
        }
    }

    private RayTracingInstanceCullingResults BuildAccelerationStructure()
    {
        RayTracingInstanceCullingConfig cullingConfig = new RayTracingInstanceCullingConfig();

        cullingConfig.flags = RayTracingInstanceCullingFlags.ComputeMaterialsCRC;

        // Disable anyhit shaders for opaque geometries for best ray tracing performance.
        cullingConfig.subMeshFlagsConfig.opaqueMaterials = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;

        // Disable transparent geometries. We don't have a definition for what a "transparent" material is, so we conservatively disable all of them.
        cullingConfig.subMeshFlagsConfig.transparentMaterials = RayTracingSubMeshFlags.Disabled;

        // Enable anyhit shaders for alpha-tested / cutout geometries.
        cullingConfig.subMeshFlagsConfig.alphaTestedMaterials = RayTracingSubMeshFlags.Enabled;

        RayTracingInstanceMaterialConfig alphaTestedMaterialsConfig = new RayTracingInstanceMaterialConfig()
        {
            optionalShaderKeywords = new string[1] { "ALPHATEST_ON" },
            renderQueueLowerBound = (int)UnityEngine.Rendering.RenderQueue.AlphaTest,
            renderQueueUpperBound = (int)UnityEngine.Rendering.RenderQueue.GeometryLast
        };

        cullingConfig.alphaTestedMaterialConfig = alphaTestedMaterialsConfig;

        cullingConfig.triangleCullingConfig.forceDoubleSided = false;
        cullingConfig.triangleCullingConfig.frontTriangleCounterClockwise = false;
        cullingConfig.triangleCullingConfig.optionalDoubleSidedShaderKeywords = new string[1] { "DOUBLE_SIDED_ON" };

        RayTracingInstanceCullingTest pathTracingTest = new RayTracingInstanceCullingTest();
        pathTracingTest.allowOpaqueMaterials      = true;
        pathTracingTest.allowTransparentMaterials = true;
        pathTracingTest.allowAlphaTestedMaterials = true;
        pathTracingTest.layerMask                 = -1;
        pathTracingTest.shadowCastingModeMask     = (1 << (int)ShadowCastingMode.Off) | (1 << (int)ShadowCastingMode.On) | (1 << (int)ShadowCastingMode.TwoSided);
        pathTracingTest.instanceMask              = 0xFF;

        cullingConfig.instanceTests = new RayTracingInstanceCullingTest[] { pathTracingTest };

        rayTracingAccelerationStructure.ClearInstances();
        RayTracingInstanceCullingResults cullingResult = rayTracingAccelerationStructure.CullInstances(ref cullingConfig);
        rayTracingAccelerationStructure.Build();

        return cullingResult;
    }

    private void ReleaseResources()
    {
        if (rayTracingAccelerationStructure != null)
        {
            rayTracingAccelerationStructure.Release();
            rayTracingAccelerationStructure = null;
        }

        if (rayTracingOutput != null)
        {
            rayTracingOutput.Release();
            rayTracingOutput = null;
        }

        if (lightsBuffer != null)
        {
            lightsBuffer.Release();
            lightsBuffer = null;
        }

        cameraWidth = 0;
        cameraHeight = 0;
    }

    private static void SetPunctualColor(ref LightData data, Light light)
    {
        Color c = light.color.linear * light.intensity;
        data.color = new Vector3(c.r, c.g, c.b);
    }

    private int CollectLights()
    {
        lightsScratch.Clear();

        Light[] unityLights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (Light light in unityLights)
        {
            if (!light.isActiveAndEnabled)
                continue;

            LightData data = new LightData();

            switch (light.type)
            {
                case LightType.Directional:
                    data.type = 0;
                    data.direction = light.transform.forward;
                    SetPunctualColor(ref data, light);
                    break;
                case LightType.Point:
                    data.type = 1;
                    data.position = light.transform.position;
                    data.range = light.range;
                    SetPunctualColor(ref data, light);
                    break;
                default:
                    continue;
            }

            lightsScratch.Add(data);
        }

        int count = lightsScratch.Count;

        int requiredCapacity = Mathf.Max(count, 1);
        if (lightsBuffer == null || lightsBuffer.count < requiredCapacity)
        {
            if (lightsBuffer != null)
                lightsBuffer.Release();
            lightsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, requiredCapacity, LIGHT_DATA_STRIDE);
        }

        if (count > 0)
            lightsBuffer.SetData(lightsScratch);
        else
            lightsBuffer.SetData(DummyLights);

        return count;
    }

    private int ComputeLightHash()
    {
        int h = 17;
        h = h * 31 + lightsScratch.Count;
        foreach (LightData l in lightsScratch)
        {
            h = h * 31 + l.color.GetHashCode();
            h = h * 31 + (int)l.type;
            h = h * 31 + l.direction.GetHashCode();
            h = h * 31 + l.range.GetHashCode();
            h = h * 31 + l.position.GetHashCode();
        }
        return h;
    }

    private void CreateResources()
    {
        CreateRayTracingAccelerationStructure();

        if (cameraWidth != Camera.main.pixelWidth || cameraHeight != Camera.main.pixelHeight)
        {
            if (rayTracingOutput)
                rayTracingOutput.Release();

            RenderTextureDescriptor rtDesc = new RenderTextureDescriptor()
            {
                dimension = TextureDimension.Tex2D,
                width = Camera.main.pixelWidth,
                height = Camera.main.pixelHeight,
                depthBufferBits = 0,
                volumeDepth = 1,
                msaaSamples = 1,
                vrUsage = VRTextureUsage.OneEye,
                graphicsFormat = GraphicsFormat.R32G32B32A32_SFloat,
                enableRandomWrite = true,
            };

            rayTracingOutput = new RenderTexture(rtDesc);
            rayTracingOutput.Create();

            cameraWidth = (uint)Camera.main.pixelWidth;
            cameraHeight = (uint)Camera.main.pixelHeight;

            convergenceTracker.Reset();
        }
    }

    void OnDestroy()
    {
        ReleaseResources();
    }

    void OnDisable()
    {
        ReleaseResources();
    }

    private void Update()
    {
        CreateResources();

        if (Input.GetKeyDown("space"))
            convergenceTracker.Reset();
    }

    [ImageEffectOpaque]
    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (!SystemInfo.supportsRayTracing || !rayTracingShader)
        {
            Debug.Log("The RayTracing API is not supported by this GPU or by the current graphics API.");
            Graphics.Blit(src, dest);
            return;
        }

        if (rayTracingAccelerationStructure == null)
        {
            Graphics.Blit(src, dest);
            return;
        }

        int lightCount = CollectLights();
        int lightHash = ComputeLightHash();

        RayTracingInstanceCullingResults cullingResult = BuildAccelerationStructure();
        uint instanceCount = rayTracingAccelerationStructure.GetInstanceCount();

        convergenceTracker.DetectInvalidation(Camera.main, bounceCountOpaque, bounceCountTransparent, debugSingleBounce, debugBounceIndex, lightHash, instanceCount, cullingResult);

        rayTracingShader.SetShaderPass("PathTracing");

        // Cap at 254 because RayPayload.bounceIndices packs each counter into a single byte
        // and reserves 0xff as the terminated-path sentinel.
        Shader.SetGlobalInt(Shader.PropertyToID("g_MaxBounceCountOpaque"), (int)System.Math.Min(bounceCountOpaque, 254u));
        Shader.SetGlobalInt(Shader.PropertyToID("g_MaxBounceCountTransparent"), (int)System.Math.Min(bounceCountTransparent, 254u));
        Shader.SetGlobalBuffer(Shader.PropertyToID("g_Lights"), lightsBuffer);
        Shader.SetGlobalInt(Shader.PropertyToID("g_LightCount"), lightCount);

        // Input
        rayTracingShader.SetAccelerationStructure(Shader.PropertyToID("g_AccelStruct"), rayTracingAccelerationStructure);
        rayTracingShader.SetFloat(Shader.PropertyToID("g_Zoom"), Mathf.Tan(Mathf.Deg2Rad * Camera.main.fieldOfView * 0.5f));
        rayTracingShader.SetFloat(Shader.PropertyToID("g_AspectRatio"), cameraWidth / (float)cameraHeight);
        rayTracingShader.SetInt(Shader.PropertyToID("g_ConvergenceStep"), convergenceTracker.Step);
        rayTracingShader.SetInt(Shader.PropertyToID("g_FrameIndex"), Time.frameCount);
        rayTracingShader.SetInt(Shader.PropertyToID("g_DebugBounceIndex"), (int)debugBounceIndex);
        rayTracingShader.SetTexture(Shader.PropertyToID("g_EnvTex"), envTexture);

        if (debugSingleBounce)
            Shader.EnableKeyword("DEBUG_SINGLE_BOUNCE");
        else
            Shader.DisableKeyword("DEBUG_SINGLE_BOUNCE");

        // Output
        rayTracingShader.SetTexture(Shader.PropertyToID("g_Radiance"), rayTracingOutput);

        rayTracingShader.Dispatch("MainRayGenShader", (int)cameraWidth, (int)cameraHeight, 1, Camera.main);

        Graphics.Blit(rayTracingOutput, dest);

        convergenceTracker.Advance();
    }
}
