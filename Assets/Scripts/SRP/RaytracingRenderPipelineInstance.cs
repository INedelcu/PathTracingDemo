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
    
    public RayTracingAccelerationStructure rayTracingAccelerationStructure = null;
    
    private void ReleaseResources()
    {
     
    }

    private void CreateResources(Camera camera)
    {
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

            var additionalData = camera.GetComponent<AdditionalCameraData>();
            if (additionalData == null)
            {
                additionalData = camera.gameObject.AddComponent<AdditionalCameraData>();
                additionalData.hideFlags = HideFlags.HideAndDontSave;   // Don't show this in inspector
            }
            additionalData.CreatePersistentResources(camera);

            if (!renderPipelineAsset.rayTracingShader || !renderPipelineAsset.rayTracingShaderGBuffer)
            {
                Debug.LogError("No RayTracing shader!");
                return;
            }
            
            if (rayTracingAccelerationStructure == null)
                return;

            camera.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;

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
            renderPipelineAsset.rayTracingShaderGBuffer.SetTexture(Shader.PropertyToID("g_GBufferWorldNormals"), additionalData.gBufferWorldNormals);
            renderPipelineAsset.rayTracingShaderGBuffer.SetTexture(Shader.PropertyToID("g_GBufferIntersectionT"), additionalData.gBufferIntersectionT);
            renderPipelineAsset.rayTracingShaderGBuffer.SetTexture(Shader.PropertyToID("g_GBufferMotionVectors"), additionalData.gBufferMotionVectors);

            commandBuffer.DispatchRays(renderPipelineAsset.rayTracingShaderGBuffer, "MainRayGenShader", (uint)camera.pixelWidth, (uint)camera.pixelHeight, 1, camera);
    

            ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
            // Path tracing
            renderPipelineAsset.rayTracingShader.SetShaderPass("PathTracing");

            // Input
            renderPipelineAsset.rayTracingShader.SetAccelerationStructure(Shader.PropertyToID("g_AccelStruct"), rayTracingAccelerationStructure);
            renderPipelineAsset.rayTracingShader.SetFloat(Shader.PropertyToID("g_Zoom"), Mathf.Tan(Mathf.Deg2Rad * camera.fieldOfView * 0.5f));
            renderPipelineAsset.rayTracingShader.SetFloat(Shader.PropertyToID("g_AspectRatio"), camera.pixelWidth / (float)camera.pixelHeight);
            renderPipelineAsset.rayTracingShader.SetInt(Shader.PropertyToID("g_FrameIndex"), additionalData.frameIndex);
            renderPipelineAsset.rayTracingShader.SetTexture(Shader.PropertyToID("g_EnvTex"), renderPipelineAsset.envTexture);
            renderPipelineAsset.rayTracingShader.SetMatrix(Shader.PropertyToID("g_PreviousViewProjection"), additionalData.previousViewProjection);

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
            renderPipelineAsset.rayTracingShader.SetTexture(Shader.PropertyToID("g_Radiance"), additionalData.rayTracingOutput);
            renderPipelineAsset.rayTracingShader.SetTexture(Shader.PropertyToID("g_RadianceHistory"), additionalData.colorHistory);

            commandBuffer.DispatchRays(renderPipelineAsset.rayTracingShader, "MainRayGenShader", (uint)camera.pixelWidth, (uint)camera.pixelHeight, 1, camera);

            // TODO plug in the radiance variance.
            AtrousFilter(additionalData.rayTracingOutput, additionalData.gBufferWorldNormals, additionalData.gBufferIntersectionT, additionalData.rayTracingOutput);
            
            commandBuffer.Blit(additionalData.rayTracingOutput, camera.activeTexture);

            // Instruct the graphics API to perform all scheduled commands
            context.ExecuteCommandBuffer(commandBuffer);
            context.Submit();
            
            if (renderPipelineAsset.debugOutput)
            {
                // Debug RenderTextures
                const uint rtDebugCount = 3;
                RenderTexture[] renderTextures = new RenderTexture[rtDebugCount];
                renderTextures[0] = additionalData.gBufferWorldNormals;
                renderTextures[1] = additionalData.gBufferIntersectionT;
                renderTextures[2] = additionalData.gBufferMotionVectors;

                int downScaleFactor = 4;

                GL.PushMatrix();
                GL.LoadPixelMatrix(0, camera.pixelWidth, camera.pixelHeight, 0);

                int left = 0;
                int top = 0;
                for (int i = 0; i < rtDebugCount; i++)
                {
                    Graphics.DrawTexture(new Rect(left, top, renderTextures[i].width / downScaleFactor, renderTextures[i].height / downScaleFactor), renderTextures[i]);

                    if (i % downScaleFactor == (downScaleFactor - 1))
                    {
                        left = 0;
                        top += camera.pixelHeight / downScaleFactor;
                    }
                    else
                        left += camera.pixelWidth / downScaleFactor;
                }

                GL.PopMatrix();
            }

            ReleaseResources();
            
            additionalData.UpdateCameraDataPostRender(camera);
        }
        commandBuffer.Release();
    }
    
    void AtrousFilter(RenderTexture radiance, RenderTexture normals, RenderTexture depth, RenderTexture filteredRadiance)
    {
    
    }
}