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

    private uint cameraWidth = 0;
    private uint cameraHeight = 0;

    private int convergenceStep = 0;

    private Matrix4x4 prevCameraMatrix;
    private uint prevBounceCountOpaque = 0;
    private uint prevBounceCountTransparent = 0;
    private int  prevLightHash = 0;

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
                managementMode = RayTracingAccelerationStructure.ManagementMode.Automatic,
                layerMask = 255
            };
            rayTracingAccelerationStructure = new RayTracingAccelerationStructure(settings);
        }
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

            convergenceStep = 0;
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

    private void OnEnable()
    {
        prevCameraMatrix = Camera.main.cameraToWorldMatrix;
        prevBounceCountOpaque = bounceCountOpaque;
        prevBounceCountTransparent = bounceCountTransparent;
    }

    private void Update()
    {
        CreateResources();

        if (Input.GetKeyDown("space"))
            convergenceStep = 0;
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
            return;

        if (prevCameraMatrix != Camera.main.cameraToWorldMatrix)
            convergenceStep = 0;

        if (prevBounceCountOpaque != bounceCountOpaque)
            convergenceStep = 0;

        if (prevBounceCountTransparent != bounceCountTransparent)
            convergenceStep = 0;

        int lightCount = CollectLights();
        int lightHash = ComputeLightHash();
        if (lightHash != prevLightHash)
            convergenceStep = 0;

        // Not really needed per frame if the scene is static.
        rayTracingAccelerationStructure.Build();

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
        rayTracingShader.SetInt(Shader.PropertyToID("g_ConvergenceStep"), convergenceStep);
        rayTracingShader.SetInt(Shader.PropertyToID("g_FrameIndex"), Time.frameCount);
        rayTracingShader.SetTexture(Shader.PropertyToID("g_EnvTex"), envTexture);

        // Output
        rayTracingShader.SetTexture(Shader.PropertyToID("g_Radiance"), rayTracingOutput);

        rayTracingShader.Dispatch("MainRayGenShader", (int)cameraWidth, (int)cameraHeight, 1, Camera.main);

        Graphics.Blit(rayTracingOutput, dest);

        convergenceStep++;

        prevCameraMatrix            = Camera.main.cameraToWorldMatrix;
        prevBounceCountOpaque       = bounceCountOpaque;
        prevBounceCountTransparent  = bounceCountTransparent;
        prevLightHash               = lightHash;
    }
}
