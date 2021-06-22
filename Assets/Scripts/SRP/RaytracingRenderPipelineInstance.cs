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
    
    private RenderTexture rayTracingOutput = null;
    private RenderTexture gBufferWorldNormals = null;
    private RenderTexture gBufferIntersectionT = null;
    
    public RayTracingAccelerationStructure rayTracingAccelerationStructure = null;
    
    private void ReleaseResources()
    {
        if (rayTracingOutput != null)
        {
            rayTracingOutput.Release();
            rayTracingOutput = null;
        }
        if (gBufferWorldNormals != null)
        {
            gBufferWorldNormals.Release();
            gBufferWorldNormals = null;
        }
        if (gBufferIntersectionT != null)
        {
            gBufferIntersectionT.Release();
            gBufferIntersectionT = null;
        }
    }

    private void CreateResources(Camera camera)
    {
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
        }

        {
            if (gBufferWorldNormals)
                gBufferWorldNormals.Release();

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

            gBufferWorldNormals = new RenderTexture(rtDesc);
            gBufferWorldNormals.Create();
        }
        {
            if (gBufferIntersectionT)
                gBufferIntersectionT.Release();

            RenderTextureDescriptor rtDesc = new RenderTextureDescriptor()
            {
                dimension = TextureDimension.Tex2D,
                width = camera.pixelWidth,
                height = camera.pixelHeight,
                depthBufferBits = 0,
                volumeDepth = 1,
                msaaSamples = 1,
                vrUsage = VRTextureUsage.OneEye,
                graphicsFormat = GraphicsFormat.R32_SFloat,
                enableRandomWrite = true,
            };

            gBufferIntersectionT = new RenderTexture(rtDesc);
            gBufferIntersectionT.Create();
        }
    }

    protected override void Render (ScriptableRenderContext context, Camera[] cameras)
    {
        var commandBuffer = new CommandBuffer();
        if (!SystemInfo.supportsRayTracing)
        {
            Debug.Log("The RayTracing API is not supported by this GPU or by the current graphics API.");
            commandBuffer.ClearRenderTarget(true, true, Color.magenta);
            context.ExecuteCommandBuffer(commandBuffer);
            commandBuffer.Release();
            context.Submit();
            return;
        }

        commandBuffer.BuildRayTracingAccelerationStructure(rayTracingAccelerationStructure);
        
        commandBuffer.SetGlobalInteger(Shader.PropertyToID("g_BounceCountOpaque"), renderPipelineAsset.bounceCountOpaque);
        commandBuffer.SetGlobalInteger(Shader.PropertyToID("g_BounceCountTransparent"), renderPipelineAsset.bounceCountTransparent);

        // Iterate over all Cameras
        foreach (Camera camera in cameras)
        {
            CreateResources(camera);
                
            if (!renderPipelineAsset.rayTracingShader || !renderPipelineAsset.rayTracingShaderGBuffer)
            {
                Debug.LogError("No RayTracing shader!");
                return;
            }
            
            if (rayTracingAccelerationStructure == null)
                return;
            
            // Update the value of built-in shader variables, based on the current Camera
            context.SetupCameraProperties(camera);

            ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
            // Generate GBuffer for denoising input.
            renderPipelineAsset.rayTracingShaderGBuffer.SetShaderPass("PathTracingGBuffer");

            // Input
            renderPipelineAsset.rayTracingShaderGBuffer.SetAccelerationStructure(Shader.PropertyToID("g_AccelStruct"), rayTracingAccelerationStructure);
            renderPipelineAsset.rayTracingShaderGBuffer.SetFloat(Shader.PropertyToID("g_Zoom"), Mathf.Tan(Mathf.Deg2Rad * camera.fieldOfView * 0.5f));
            renderPipelineAsset.rayTracingShaderGBuffer.SetFloat(Shader.PropertyToID("g_AspectRatio"), camera.pixelWidth / (float)camera.pixelHeight);

            // Output
            renderPipelineAsset.rayTracingShaderGBuffer.SetTexture(Shader.PropertyToID("g_WorldNormals"), gBufferWorldNormals);
            renderPipelineAsset.rayTracingShaderGBuffer.SetTexture(Shader.PropertyToID("g_IntersectionT"), gBufferIntersectionT);

            commandBuffer.DispatchRays(renderPipelineAsset.rayTracingShaderGBuffer, "MainRayGenShader", (uint)camera.pixelWidth, (uint)camera.pixelHeight, 1, camera);
    

            ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
            // Path tracing
            renderPipelineAsset.rayTracingShader.SetShaderPass("PathTracing");

            // Input
            renderPipelineAsset.rayTracingShader.SetAccelerationStructure(Shader.PropertyToID("g_AccelStruct"), rayTracingAccelerationStructure);
            renderPipelineAsset.rayTracingShader.SetFloat(Shader.PropertyToID("g_Zoom"), Mathf.Tan(Mathf.Deg2Rad * camera.fieldOfView * 0.5f));
            renderPipelineAsset.rayTracingShader.SetFloat(Shader.PropertyToID("g_AspectRatio"), camera.pixelWidth / (float)camera.pixelHeight);
            renderPipelineAsset.rayTracingShader.SetInt(Shader.PropertyToID("g_FrameIndex"), Time.frameCount);
            renderPipelineAsset.rayTracingShader.SetTexture(Shader.PropertyToID("g_EnvTex"), renderPipelineAsset.envTexture);

            Light dirLight = Object.FindObjectOfType<Light>();
            if(dirLight && dirLight.type==LightType.Directional)
            {
                //Debug.Log("found dir light"+dirLight.type);
                //Debug.Log("found dir light"+dirLight.transform.forward);

                int castShadows = dirLight.shadows != LightShadows.None ? 1:0;

                renderPipelineAsset.rayTracingShader.SetInt(Shader.PropertyToID("g_UseDirectionalLight"), 1);
                renderPipelineAsset.rayTracingShader.SetInt(Shader.PropertyToID("g_DirectionalLightShadows"), castShadows);
                Color lightColor = dirLight.color * dirLight.intensity;
                renderPipelineAsset.rayTracingShader.SetVector(Shader.PropertyToID("g_DirectionalLight"), new Vector4(dirLight.transform.forward.x,dirLight.transform.forward.y,dirLight.transform.forward.z, 0.0f));
                renderPipelineAsset.rayTracingShader.SetVector(Shader.PropertyToID("g_DirectionalLightColor"), new Vector4(lightColor.r, lightColor.g, lightColor.b, 0.0f));
            }
            else
            {
                renderPipelineAsset.rayTracingShader.SetInt(Shader.PropertyToID("g_UseDirectionalLight"), 0);
            }

            // Output
            renderPipelineAsset.rayTracingShader.SetTexture(Shader.PropertyToID("g_Radiance"), rayTracingOutput);       

            commandBuffer.DispatchRays(renderPipelineAsset.rayTracingShader, "MainRayGenShader", (uint)camera.pixelWidth, (uint)camera.pixelHeight, 1, camera);

            // TODO plug in the radiance variance.
            AtrousFilter(rayTracingOutput, gBufferWorldNormals, gBufferIntersectionT, rayTracingOutput);
            
            commandBuffer.Blit(rayTracingOutput, camera.activeTexture);

            // Instruct the graphics API to perform all scheduled commands
            context.ExecuteCommandBuffer(commandBuffer);
            context.Submit();
            
            ReleaseResources();
        }
        commandBuffer.Release();
    }
    
    void AtrousFilter(RenderTexture radiance, RenderTexture normals, RenderTexture depth, RenderTexture filteredRadiance)
    {
    
    }
}