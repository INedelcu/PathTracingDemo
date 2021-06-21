using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
    
public class RaytracingRenderPipelineInstance : RenderPipeline
{
    // Use this variable to a reference to the Render Pipeline Asset that was passed to the constructor
    private RaytracingRenderPipelineAsset renderPipelineAsset;

    public RaytracingRenderPipelineInstance(RaytracingRenderPipelineAsset asset)
    {
        renderPipelineAsset = asset;
        
        if (rayTracingAccelerationStructure == null)
        {
            RayTracingAccelerationStructure.RASSettings settings = new RayTracingAccelerationStructure.RASSettings();
            settings.rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything;
            settings.managementMode = RayTracingAccelerationStructure.ManagementMode.Automatic;
            settings.layerMask = 255;

            rayTracingAccelerationStructure = new RayTracingAccelerationStructure(settings);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (rayTracingAccelerationStructure != null)
        {
            rayTracingAccelerationStructure.Release();
            rayTracingAccelerationStructure = null;
        }
        ReleaseResources();
    }
    
    private uint cameraWidth = 0;
    private uint cameraHeight = 0;
    
    private int convergenceStep = 0;

    private Matrix4x4 prevCameraMatrix;
    private uint prevBounceCountOpaque = 0;
    private uint prevBounceCountTransparent = 0;

    private RenderTexture rayTracingOutput = null;
    
    public RayTracingAccelerationStructure rayTracingAccelerationStructure = null;
    
    private void ReleaseResources()
    {
        if (rayTracingOutput != null)
        {
            rayTracingOutput.Release();
            rayTracingOutput = null;
        }
     
        cameraWidth = 0;
        cameraHeight = 0;
    }

    private void CreateResources(Camera camera)
    {
        if (cameraWidth != camera.pixelWidth || cameraHeight != camera.pixelHeight)
        {
            if (rayTracingOutput)
                rayTracingOutput.Release();

            RenderTextureDescriptor rtDesc = new RenderTextureDescriptor()
            {
                dimension = TextureDimension.Tex2D,
                width = camera.pixelWidth,
                height = camera.pixelHeight,
                depthBufferBits = 0,
                volumeDepth = 1,
                msaaSamples = 1,
                vrUsage = VRTextureUsage.OneEye,
                graphicsFormat = GraphicsFormat.R32G32B32A32_SFloat,
                enableRandomWrite = true,
            };

            rayTracingOutput = new RenderTexture(rtDesc);
            rayTracingOutput.Create();

            cameraWidth = (uint)camera.pixelWidth;
            cameraHeight = (uint)camera.pixelHeight;

            convergenceStep = 0;
        }
    }
    
    private void Update()
    {
        if (Input.GetKeyDown("space"))
            convergenceStep = 0;
    }

    protected override void Render (ScriptableRenderContext context, Camera[] cameras)
    {
        if (!SystemInfo.supportsRayTracing)
        {
            Debug.Log("The RayTracing API is not supported by this GPU or by the current graphics API.");
            return;
        }
        
        // Create and schedule a command to clear the current render target
        var commandBuffer = new CommandBuffer();
        commandBuffer.ClearRenderTarget(true, true, Color.red); // remove this when everything works
        commandBuffer.BuildRayTracingAccelerationStructure(rayTracingAccelerationStructure);

        // Iterate over all Cameras
        foreach (Camera camera in cameras)
        {
            CreateResources(camera);
                
            if (!renderPipelineAsset.rayTracingShader)
            {
                Debug.LogError("No RayTracing shader!");
                return;
            }
            
            if (rayTracingAccelerationStructure == null)
                return;
            
            // Update the value of built-in shader variables, based on the current Camera
            context.SetupCameraProperties(camera);

            // Schedule a command to draw the Skybox if required
            if (camera.clearFlags == CameraClearFlags.Skybox && RenderSettings.skybox != null)
            {
                context.DrawSkybox(camera);
            }
            
            if (prevCameraMatrix != camera.cameraToWorldMatrix)
                convergenceStep = 0;

            if (prevBounceCountOpaque != renderPipelineAsset.bounceCountOpaque)
                convergenceStep = 0;

            if (prevBounceCountTransparent != renderPipelineAsset.bounceCountTransparent)
                convergenceStep = 0;

            renderPipelineAsset.rayTracingShader.SetShaderPass("PathTracing");

            Shader.SetGlobalInt(Shader.PropertyToID("g_BounceCountOpaque"), (int)renderPipelineAsset.bounceCountOpaque);
            Shader.SetGlobalInt(Shader.PropertyToID("g_BounceCountTransparent"), (int)renderPipelineAsset.bounceCountTransparent);

            // Input
            renderPipelineAsset.rayTracingShader.SetAccelerationStructure(Shader.PropertyToID("g_AccelStruct"), rayTracingAccelerationStructure);
            renderPipelineAsset.rayTracingShader.SetFloat(Shader.PropertyToID("g_Zoom"), Mathf.Tan(Mathf.Deg2Rad * camera.fieldOfView * 0.5f));
            renderPipelineAsset.rayTracingShader.SetFloat(Shader.PropertyToID("g_AspectRatio"), cameraWidth / (float)cameraHeight);
            renderPipelineAsset.rayTracingShader.SetInt(Shader.PropertyToID("g_ConvergenceStep"), convergenceStep);
            renderPipelineAsset.rayTracingShader.SetInt(Shader.PropertyToID("g_FrameIndex"), Time.frameCount);
            renderPipelineAsset.rayTracingShader.SetTexture(Shader.PropertyToID("g_EnvTex"), renderPipelineAsset.envTexture);

            // Output
            renderPipelineAsset.rayTracingShader.SetTexture(Shader.PropertyToID("g_Radiance"), rayTracingOutput);       

            commandBuffer.DispatchRays(renderPipelineAsset.rayTracingShader, "MainRayGenShader", cameraWidth, cameraHeight, 1, camera);
           
            commandBuffer.Blit(rayTracingOutput, camera.activeTexture);

            convergenceStep++;

            prevCameraMatrix            = camera.cameraToWorldMatrix;
            prevBounceCountOpaque       = renderPipelineAsset.bounceCountOpaque;
            prevBounceCountTransparent  = renderPipelineAsset.bounceCountTransparent;
            
            // Instruct the graphics API to perform all scheduled commands
            context.ExecuteCommandBuffer(commandBuffer);
            commandBuffer.Release();
            context.Submit();
            
            ReleaseResources();
        }
    }
}