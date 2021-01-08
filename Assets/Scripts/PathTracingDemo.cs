using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

[ExecuteInEditMode]
public class PathTracingDemo : MonoBehaviour
{
    public RayTracingShader rayTracingShader = null;

    public Cubemap envTexture = null;

    // bounceCountOpaque + bounceCountTransparent ranges should not exceed 31

    [Range(1, 10)]
    public uint bounceCountOpaque = 5;

    [Range(1, 20)]
    public uint bounceCountTransparent = 8;
    
    private uint cameraWidth = 0;
    private uint cameraHeight = 0;
    
    private int convergenceStep = 0;

    private Matrix4x4 prevCameraMatrix;
    private uint prevBounceCountOpaque = 0;
    private uint prevBounceCountTransparent = 0;

    private RenderTexture rayTracingOutput = null;
    
    private RayTracingAccelerationStructure rayTracingAccelerationStructure = null;

    private void CreateRayTracingAccelerationStructure()
    {
        if (rayTracingAccelerationStructure == null)
        {
            RayTracingAccelerationStructure.RASSettings settings = new RayTracingAccelerationStructure.RASSettings();
            settings.rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything;
            settings.managementMode = RayTracingAccelerationStructure.ManagementMode.Automatic;
            settings.layerMask = 255;

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
     
        cameraWidth = 0;
        cameraHeight = 0;
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

        // Not really needed per frame if the scene is static.
        rayTracingAccelerationStructure.Build();

        rayTracingShader.SetShaderPass("PathTracing");

        // Input
        rayTracingShader.SetAccelerationStructure(Shader.PropertyToID("g_AccelStruct"), rayTracingAccelerationStructure);
        rayTracingShader.SetFloat(Shader.PropertyToID("g_Zoom"), Mathf.Tan(Mathf.Deg2Rad * Camera.main.fieldOfView * 0.5f));
        rayTracingShader.SetFloat(Shader.PropertyToID("g_AspectRatio"), cameraWidth / (float)cameraHeight);
        rayTracingShader.SetInt(Shader.PropertyToID("g_ConvergenceStep"), convergenceStep);
        rayTracingShader.SetInt(Shader.PropertyToID("g_FrameIndex"), Time.frameCount);
        rayTracingShader.SetInt(Shader.PropertyToID("g_BounceCountOpaque"), (int)bounceCountOpaque);
        rayTracingShader.SetInt(Shader.PropertyToID("g_BounceCountTransparent"), (int)bounceCountTransparent);
        rayTracingShader.SetTexture(Shader.PropertyToID("g_EnvTex"), envTexture);

        // Output
        rayTracingShader.SetTexture(Shader.PropertyToID("g_Radiance"), rayTracingOutput);       

        rayTracingShader.Dispatch("MainRayGenShader", (int)cameraWidth, (int)cameraHeight, 1, Camera.main);
       
        Graphics.Blit(rayTracingOutput, dest);

        convergenceStep++;

        prevCameraMatrix            = Camera.main.cameraToWorldMatrix;
        prevBounceCountOpaque       = bounceCountOpaque;
        prevBounceCountTransparent  = bounceCountTransparent;
    }
}
