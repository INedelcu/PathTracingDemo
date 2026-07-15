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

    [Tooltip("Flag pixels whose sample is non-finite (NaN or Inf) in bright magenta instead of accumulating them, to locate the source of bad samples. The flag is sticky for the rest of convergence.")]
    public bool debugValidate = false;

    private uint cameraWidth = 0;
    private uint cameraHeight = 0;

    private readonly ConvergenceStateTracker convergenceTracker = new ConvergenceStateTracker();

    private RenderTexture rayTracingOutput = null;

    private RayTracingAccelerationStructure rayTracingAccelerationStructure = null;

    // GGX single scatter directional albedo LUT for multiple scattering energy compensation.
    private Texture2D energyCompLUT = null;

    // Layout must match the Light struct in Assets/Shaders/Lights.hlsl (48 bytes).
    [StructLayout(LayoutKind.Sequential)]
    private struct LightData
    {
        public Vector3 color;
        public uint    type;        // 0 = directional, 1 = point
        public Vector3 direction;   // Directional only. Unit length (normalized in CollectLights).
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

        if (energyCompLUT != null)
        {
            if (Application.isPlaying)
                Destroy(energyCompLUT);
            else
                DestroyImmediate(energyCompLUT);
            energyCompLUT = null;
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
                    data.direction = light.transform.forward.normalized;
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
    
    private static float SmithG1(float NdotV, float a)
    {
        float a2 = a * a;
        return 2.0f * NdotV / Mathf.Max(NdotV + Mathf.Sqrt(a2 + (1.0f - a2) * NdotV * NdotV), 1e-7f);
    }

    private static float SmithG2(float NdotL, float NdotV, float a)
    {
        float a2 = a * a;
        float lambdaV = NdotL * Mathf.Sqrt(a2 + (1.0f - a2) * NdotV * NdotV);
        float lambdaL = NdotV * Mathf.Sqrt(a2 + (1.0f - a2) * NdotL * NdotL);
        return 2.0f * NdotL * NdotV / Mathf.Max(lambdaV + lambdaL, 1e-7f);
    }

    private static Vector3 SampleGGXVNDF(Vector3 Ve, float alpha, float u1, float u2)
    {
        Vector3 Vh = new Vector3(alpha * Ve.x, alpha * Ve.y, Ve.z).normalized;
        float lensq = Vh.x * Vh.x + Vh.y * Vh.y;
        Vector3 T1 = lensq > 0.0f ? new Vector3(-Vh.y, Vh.x, 0.0f) * (1.0f / Mathf.Sqrt(lensq)) : new Vector3(1, 0, 0);
        Vector3 T2 = Vector3.Cross(Vh, T1);
        float r = Mathf.Sqrt(u1);
        float phi = 2.0f * Mathf.PI * u2;
        float t1 = r * Mathf.Cos(phi);
        float t2 = r * Mathf.Sin(phi);
        float s = 0.5f * (1.0f + Vh.z);
        t2 = (1.0f - s) * Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - t1 * t1)) + s * t2;
        Vector3 Nh = t1 * T1 + t2 * T2 + Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - t1 * t1 - t2 * t2)) * Vh;
        return new Vector3(alpha * Nh.x, alpha * Nh.y, Mathf.Max(0.0f, Nh.z)).normalized;
    }

    // Van der Corput radical inverse (base 2), the second Hammersley coordinate.
    private static float RadicalInverseVdC(uint bits)
    {
        bits = (bits << 16) | (bits >> 16);
        bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
        bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
        bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
        bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
        return bits * 2.3283064365386963e-10f; // bits * 1 / 2^32
    }

    private static float GGXDirectionalAlbedo(float NdotV, float alpha, int sampleCount)
    {
        Vector3 V = new Vector3(Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - NdotV * NdotV)), 0.0f, NdotV);
        float g1 = SmithG1(NdotV, alpha);
        float sum = 0.0f;
        for (int i = 0; i < sampleCount; i++)
        {
            Vector3 H = SampleGGXVNDF(V, alpha, (i + 0.5f) / sampleCount, RadicalInverseVdC((uint)i));
            float VdotH = Vector3.Dot(V, H);
            Vector3 L = 2.0f * VdotH * H - V; // reflect(-V, H)
            if (L.z > 0.0f)
                sum += SmithG2(L.z, NdotV, alpha) / Mathf.Max(g1, 1e-7f);
        }
        return sum / sampleCount;
    }

    // --- Multiple-scattering energy compensation LUT ------------------------------
    // Bakes E_ss(NdotV, perceptualRoughness): the GGX single-scatter directional albedo
    // with Fresnel = 1, i.e. the mean of G2 / G1 over VNDF samples. 1 - E_ss is the
    // energy the single-scatter specular lobe loses to ignored multiple bounces;
    // Shading.hlsl scales the lobe by 1 + Favg (1 - E_ss) / E_ss to restore it. The GGX
    // terms below mirror BRDF.hlsl exactly; a Hammersley sequence keeps the LUT smooth
    // at a low sample count.
    private Texture2D BakeEnergyCompLUT()
    {
        const int size = 64;
        const int sampleCount = 2048;

        Texture2D lut = new Texture2D(size, size, TextureFormat.RHalf, false, true)
        {
            name = "GGX Energy Compensation LUT",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        Color[] pixels = new Color[size * size];
        for (int j = 0; j < size; j++)
        {
            // Row = perceptual roughness
			// alpha = roughness^2 to match SmoothnessToAlpha
            float roughness = (j + 0.5f) / size;
            float alpha = Mathf.Max(roughness * roughness, 1e-4f);
            for (int i = 0; i < size; i++)
            {
                float NdotV = Mathf.Max((i + 0.5f) / size, 1e-3f);
                pixels[j * size + i] = new Color(GGXDirectionalAlbedo(NdotV, alpha, sampleCount), 0.0f, 0.0f, 0.0f);
            }
        }

        lut.SetPixels(pixels);
        lut.Apply(false, true);
        return lut;
    }

    private void CreateResources()
    {
        CreateRayTracingAccelerationStructure();

        if (energyCompLUT == null)
            energyCompLUT = BakeEnergyCompLUT();

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

        convergenceTracker.DetectInvalidation(Camera.main, bounceCountOpaque, bounceCountTransparent, debugSingleBounce, debugBounceIndex, debugValidate, lightHash, instanceCount, envTexture, cullingResult);

        rayTracingShader.SetShaderPass("PathTracing");

        // Cap at 254 because RayPayload.bounceIndices packs each counter into a single byte
        // and reserves 0xff as the terminated-path sentinel.
        Shader.SetGlobalInt(Shader.PropertyToID("g_MaxBounceCountOpaque"), (int)System.Math.Min(bounceCountOpaque, 254u));
        Shader.SetGlobalInt(Shader.PropertyToID("g_MaxBounceCountTransparent"), (int)System.Math.Min(bounceCountTransparent, 254u));
        Shader.SetGlobalBuffer(Shader.PropertyToID("g_Lights"), lightsBuffer);
        Shader.SetGlobalInt(Shader.PropertyToID("g_LightCount"), lightCount);
        Shader.SetGlobalTexture(Shader.PropertyToID("g_EnergyCompLUT"), energyCompLUT);

        // Input
        rayTracingShader.SetAccelerationStructure(Shader.PropertyToID("g_AccelStruct"), rayTracingAccelerationStructure);
        rayTracingShader.SetFloat(Shader.PropertyToID("g_Zoom"), Mathf.Tan(Mathf.Deg2Rad * Camera.main.fieldOfView * 0.5f));
        rayTracingShader.SetFloat(Shader.PropertyToID("g_AspectRatio"), cameraWidth / (float)cameraHeight);
        rayTracingShader.SetInt(Shader.PropertyToID("g_ConvergenceStep"), convergenceTracker.Step);
        rayTracingShader.SetInt(Shader.PropertyToID("g_FrameIndex"), Time.frameCount);
        rayTracingShader.SetInt(Shader.PropertyToID("g_DebugBounceIndex"), (int)debugBounceIndex);
        rayTracingShader.SetTexture(Shader.PropertyToID("g_EnvTex"), envTexture);

        if (debugSingleBounce)
            rayTracingShader.EnableKeyword("DEBUG_SINGLE_BOUNCE");
        else
            rayTracingShader.DisableKeyword("DEBUG_SINGLE_BOUNCE");

        if (debugValidate)
            rayTracingShader.EnableKeyword("DEBUG_VALIDATE");
        else
            rayTracingShader.DisableKeyword("DEBUG_VALIDATE");

        // Output
        rayTracingShader.SetTexture(Shader.PropertyToID("g_Radiance"), rayTracingOutput);

        rayTracingShader.Dispatch("MainRayGenShader", (int)cameraWidth, (int)cameraHeight, 1, Camera.main);

        Graphics.Blit(rayTracingOutput, dest);

        convergenceTracker.Advance();
    }
}
