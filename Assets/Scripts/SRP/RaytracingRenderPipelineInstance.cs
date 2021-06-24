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

        ditheredTextureSet = DitheredTextureSet8SPP();
        if(asset.regenerateEnvSamplingPoints)
        {
            generateEnvSamplingPoints();
            asset.regenerateEnvSamplingPoints = false;   
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
	private DitheredTextureSet ditheredTextureSet;    
    public RayTracingAccelerationStructure rayTracingAccelerationStructure = null;
    public Texture2D envSamplingTexture;
    
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
        commandBuffer.SetGlobalInteger(Shader.PropertyToID("g_SampleCount"), renderPipelineAsset.sampleCount);

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

            if (!renderPipelineAsset.rayTracingShader)
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
            // Combined path tracing and GBuffer pass
            commandBuffer.SetRayTracingShaderPass(renderPipelineAsset.rayTracingShader, "PathTracing");

            // Input
            commandBuffer.SetRayTracingAccelerationStructure(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_AccelStruct"), rayTracingAccelerationStructure);
            commandBuffer.SetRayTracingFloatParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_Zoom"), Mathf.Tan(Mathf.Deg2Rad * camera.fieldOfView * 0.5f));
            commandBuffer.SetRayTracingFloatParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_AspectRatio"), camera.pixelWidth / (float)camera.pixelHeight);
            commandBuffer.SetRayTracingIntParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_FrameIndex"), additionalData.frameIndex);
            commandBuffer.SetRayTracingTextureParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_EnvTex"), renderPipelineAsset.envTexture);
            commandBuffer.SetRayTracingTextureParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_EnvTexSampling"), envSamplingTexture);
            commandBuffer.SetRayTracingMatrixParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_PreviousViewProjection"), additionalData.previousViewProjection);
            commandBuffer.SetRayTracingIntParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_EnableAccumulation"), renderPipelineAsset.enableAccumulation ? 1: 0);
            commandBuffer.SetRayTracingIntParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_EnableMotionVectors"), renderPipelineAsset.useMotionVectors ? 1 : 0);
            commandBuffer.SetRayTracingIntParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_EnableSubPixelJittering"), renderPipelineAsset.enableSubPixelJittering ? 1 : 0);
            commandBuffer.SetRayTracingVectorParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_Alpha"), new Vector4(renderPipelineAsset.alpha, renderPipelineAsset.speedAdaptation, 0, 1));
            commandBuffer.SetRayTracingTextureParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_GBufferDepth"), additionalData.gBufferIntersectionT);
            commandBuffer.SetRayTracingTextureParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_GBufferDepthHistory"), additionalData.depthHistory);
            commandBuffer.SetRayTracingTextureParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_RadianceHistory"), additionalData.colorHistory);

            Light dirLight = Object.FindObjectOfType<Light>();
            if(dirLight && dirLight.type==LightType.Directional)
            {
                //Debug.Log("found dir light"+dirLight.type);
                //Debug.Log("found dir light"+dirLight.transform.forward);

                int castShadows = dirLight.shadows != LightShadows.None ? 1:0;

                commandBuffer.SetRayTracingIntParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_UseDirectionalLight"), 1);
                commandBuffer.SetRayTracingIntParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_DirectionalLightShadows"), castShadows);
                Color lightColor = dirLight.color * dirLight.intensity;
                commandBuffer.SetRayTracingVectorParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_DirectionalLight"), new Vector4(dirLight.transform.forward.x,dirLight.transform.forward.y,dirLight.transform.forward.z, 0.0f));
                commandBuffer.SetRayTracingVectorParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_DirectionalLightColor"), new Vector4(lightColor.r, lightColor.g, lightColor.b, 0.0f));
            }
            else
            {
                commandBuffer.SetRayTracingIntParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_UseDirectionalLight"), 0);
            }

            BindDitheredTextureSet(commandBuffer, ditheredTextureSet);

            // Path tracer Output
            commandBuffer.SetRayTracingTextureParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_Radiance"), additionalData.rayTracingOutput);
            
            // Gbuffer Output
            commandBuffer.SetRayTracingTextureParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_GBufferWorldNormals"), additionalData.gBufferWorldNormals);
            commandBuffer.SetRayTracingTextureParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_GBufferIntersectionT"), additionalData.gBufferIntersectionT);
            commandBuffer.SetRayTracingTextureParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_GBufferMotionVectors"), additionalData.gBufferMotionVectors);

            commandBuffer.DispatchRays(renderPipelineAsset.rayTracingShader, "MainRayGenShader", (uint)camera.pixelWidth, (uint)camera.pixelHeight, 1, camera);

            // Save temporal accumulation history
            commandBuffer.CopyTexture(additionalData.rayTracingOutput, additionalData.colorHistory);
            commandBuffer.CopyTexture(additionalData.gBufferIntersectionT, additionalData.depthHistory);

            AtrousFilter(
                renderPipelineAsset, 
                commandBuffer, 
                additionalData.rayTracingOutput, 
                additionalData.aTrousPingpongRadiance, 
                additionalData.aTrousVariance, 
                additionalData.aTrousPingpongVariance, 
                additionalData.gBufferWorldNormals, 
                additionalData.gBufferIntersectionT);

            // Apply AA and tonemapping.
            int currentInput = 0;
            int currentOutput = 1;
            RenderTexture[] blitList = new RenderTexture[] {additionalData.rayTracingOutput, additionalData.aTrousPingpongRadiance};
            if (camera.GetComponent<AntialiasingAsPostEffect>() && 
                camera.GetComponent<AntialiasingAsPostEffect>().isActiveAndEnabled && 
                camera.GetComponent<Tonemapping>() && 
                camera.GetComponent<Tonemapping>().isActiveAndEnabled)
            {
                camera.GetComponent<AntialiasingAsPostEffect>().Apply(commandBuffer, blitList[currentInput], blitList[currentOutput]);
                
                int temp = currentInput;
                currentInput = currentOutput;
                currentOutput = temp;
        
                camera.GetComponent<Tonemapping>()
                    .Apply(commandBuffer, blitList[currentInput], blitList[currentOutput]);
                
                temp = currentInput;
                currentInput = currentOutput;
                currentOutput = temp;
            }
            commandBuffer.Blit(blitList[currentInput], camera.activeTexture);

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

    static void AtrousFilter(
        RaytracingRenderPipelineAsset asset,
        CommandBuffer commandBuffer,
        RenderTexture radiance,
        RenderTexture pingpongRadiance,
        RenderTexture variance,
        RenderTexture pingpongVariance,
        RenderTexture normals,
        RenderTexture depth)
    {
        if (asset.EnableATrous == false)
            return;

        ComputeShader aTrousShader = asset.aTrousShader;
        int kernelIndex = aTrousShader.FindKernel("ATrousKernel");

        RenderTexture[] radianceBuffers = new RenderTexture[2] { radiance, pingpongRadiance };
        RenderTexture[] varianceBuffers = new RenderTexture[2] { variance, pingpongVariance };
        for (int i = 0; i < asset.ATrousIterations; ++i)
        {
            int level = i + 1;
            int sourceIndex = i % 2;
            int destinationIndex = level % 2;
            commandBuffer.SetComputeTextureParam(aTrousShader, kernelIndex, Shader.PropertyToID("radiance"), radianceBuffers[sourceIndex]);
            commandBuffer.SetComputeTextureParam(aTrousShader, kernelIndex, Shader.PropertyToID("normals"), normals);
            commandBuffer.SetComputeTextureParam(aTrousShader, kernelIndex, Shader.PropertyToID("depths"), depth);
            commandBuffer.SetComputeTextureParam(aTrousShader, kernelIndex, Shader.PropertyToID("previousVariance"), varianceBuffers[sourceIndex]);
            commandBuffer.SetComputeTextureParam(aTrousShader, kernelIndex, Shader.PropertyToID("destinationVariance"), varianceBuffers[destinationIndex]);
            commandBuffer.SetComputeTextureParam(aTrousShader, kernelIndex, Shader.PropertyToID("filteredRadiance"), radianceBuffers[destinationIndex]);
            commandBuffer.SetComputeFloatParam(aTrousShader, Shader.PropertyToID("radianceSigma"), asset.aTrousRadianceSigma);
            commandBuffer.SetComputeFloatParam(aTrousShader, Shader.PropertyToID("normalSigma"), asset.aTrousNormalSigma);
            commandBuffer.SetComputeFloatParam(aTrousShader, Shader.PropertyToID("depthSigma"), asset.aTrousDepthSigma);
            commandBuffer.SetComputeIntParam(aTrousShader, Shader.PropertyToID("coordOffset"), level);
            commandBuffer.SetComputeIntParam(aTrousShader, Shader.PropertyToID("FIRST_PASS"), i == 0 ? 1 : 0);
            commandBuffer.SetComputeIntParam(aTrousShader, Shader.PropertyToID("LAST_PASS"), level == asset.ATrousIterations ? 1 : 0);
            commandBuffer.SetComputeIntParam(aTrousShader, Shader.PropertyToID("width"), radiance.width);
            commandBuffer.SetComputeIntParam(aTrousShader, Shader.PropertyToID("height"), radiance.height);
            commandBuffer.SetComputeIntParam(aTrousShader, Shader.PropertyToID("radianceStopping"), asset.RadianceStopping ? 1 : 0);
            commandBuffer.SetComputeIntParam(aTrousShader, Shader.PropertyToID("normalStopping"), asset.NormalStopping ? 1 : 0);
            commandBuffer.SetComputeIntParam(aTrousShader, Shader.PropertyToID("depthStopping"), asset.DepthStopping ? 1 : 0);            

            const int groupSizeX = 8;
            const int groupSizeY = 8;
            int threadGroupX = (radiance.width + (groupSizeX - 1)) / groupSizeX;
            int threadGroupY = (radiance.height + (groupSizeY - 1)) / groupSizeY;
            commandBuffer.DispatchCompute(aTrousShader, kernelIndex, threadGroupX, threadGroupY, 1);

            if (level == asset.ATrousIterations && destinationIndex == 1)
            {
                // copy radiance across when pingpong has cause the radiance to end up in the pingpong buffer
                commandBuffer.CopyTexture(radianceBuffers[destinationIndex], radianceBuffers[sourceIndex]);
            }
        }
    }

    // Structure that holds all the dithered sampling texture that shall be binded at dispatch time.
    internal struct DitheredTextureSet
    {
        public Texture2D scramblingTile;
        public Texture2D rankingTile;
        public Texture2D scramblingTex;
        public Texture2D owenScrambled256Tex;
    }

    internal DitheredTextureSet DitheredTextureSet8SPP()
    {
        DitheredTextureSet ditheredTextureSet = new DitheredTextureSet();
        ditheredTextureSet.scramblingTile = Resources.Load<Texture2D>("Textures/CoherentNoise/ScramblingTile256SPP");
        ditheredTextureSet.rankingTile = Resources.Load<Texture2D>("Textures/CoherentNoise/RankingTile256SPP");
        //ditheredTextureSet.scramblingTile = Resources.Load<Texture2D>("Textures/CoherentNoise/ScramblingTile8SPP");
        //ditheredTextureSet.rankingTile = Resources.Load<Texture2D>("Textures/CoherentNoise/RankingTile8SPP");
        ditheredTextureSet.scramblingTex = Resources.Load<Texture2D>("Textures/CoherentNoise/ScrambleNoise");
        ditheredTextureSet.owenScrambled256Tex = Resources.Load<Texture2D>("Textures/CoherentNoise/OwenScrambledNoise256");

        return ditheredTextureSet;
    }

    internal static void BindDitheredTextureSet(CommandBuffer cmd, DitheredTextureSet ditheredTextureSet)
    {
        cmd.SetGlobalTexture(Shader.PropertyToID("_ScramblingTileXSPP"), ditheredTextureSet.scramblingTile);
        cmd.SetGlobalTexture(Shader.PropertyToID("_RankingTileXSPP"), ditheredTextureSet.rankingTile);
        cmd.SetGlobalTexture(Shader.PropertyToID("_ScramblingTexture"), ditheredTextureSet.scramblingTex);
        cmd.SetGlobalTexture(Shader.PropertyToID("_OwenScrambledTexture"), ditheredTextureSet.owenScrambled256Tex);
    }

    float Luminance(Color c)
    {
        return 0.2126f*c.r + 0.7152f*c.g + 0.0722f*c.b;
    }

    void generateEnvSamplingPoints()
    {

        
        envSamplingTexture = Resources.Load<Texture2D>("Textures/envSamplingTexture");

        
        // computeCDF2D
        int width = renderPipelineAsset.envTexture2D.width;
        int height = renderPipelineAsset.envTexture2D.height;
        float[,] cdf_2d_envmap = new float[height, width];
        float[] cdf_1d_envmap_rows = new float[height];
        float[] sum_envmap_rows = new float[height];
  
  
        // Compute CDF 2D
        for (int iy = 0; iy < height; iy++)
        {
            float lum = Luminance( renderPipelineAsset.envTexture2D.GetPixel(0,iy) );
            cdf_2d_envmap[iy,0] = lum;
            sum_envmap_rows[iy] = 0.0f;

            // cummulate all the columns in current row
            for (int ix = 1; ix < width; ix++)
            {
                lum = Luminance( renderPipelineAsset.envTexture2D.GetPixel(ix,iy) );
                cdf_2d_envmap[iy,ix] = cdf_2d_envmap[iy,ix-1] + lum;   
            }

            // Store unnormalized sum
            sum_envmap_rows[iy] = cdf_2d_envmap[iy,width-1];

            // normalize
            for (uint ix = 0; ix < width; ix++)
            {
                cdf_2d_envmap[iy,ix] /= sum_envmap_rows[iy];
            }
        }

        // Compute CDF 1D
        cdf_1d_envmap_rows[0] = sum_envmap_rows[0];
        for (uint iy = 1; iy < height; iy++)
        {
            cdf_1d_envmap_rows[iy] = cdf_1d_envmap_rows[iy-1] + sum_envmap_rows[iy];
        }
        // normalize
        for (uint iy = 0; iy < height; iy++)
        {
            cdf_1d_envmap_rows[iy] /= cdf_1d_envmap_rows[height-1];
            //Debug.Log("cdf 1d "+cdf_1d_envmap_rows[iy]);
        }




        

        for(int j=0;j<envSamplingTexture.height;j++)
        {
            for(int i=0;i<envSamplingTexture.width;i++)
            {
                // TODO generate directions here
                Color color = new Color();
                color.r = cdf_2d_envmap[i,j] * 255;
                
                int pixelNum = j*envSamplingTexture.width + i;

                float rowRand = Random.value;
                float columnRand = Random.value;

                int rowSelected = 0;
                int columnSelected = 0;

                // TODO dichotomy
                for(int rowNum=0; rowNum<height; rowNum++)
                {
                    if(cdf_1d_envmap_rows[rowNum] > rowRand)
                    {
                        rowSelected = rowNum;
                        break;
                    }
                }

                // TODO dichotomy
                for(int columnNum=0; columnNum<width; columnNum++)
                {
                    if(cdf_2d_envmap[rowSelected, columnNum] > columnRand)
                    {
                        columnSelected = columnNum;
                        break;
                    }
                }

                //Debug.Log("rand "+rowRand+" rand "+columnRand+" row "+rowSelected+" column "+columnSelected);

                //color = renderPipelineAsset.envTexture.GetPixel(0, i, j);
                
                float u = 0.0f;
                float v = 0.0f;
                              
                //color = renderPipelineAsset.envTexture2D.GetPixel(columnSelected, rowSelected);

                u = columnSelected;
                u = u/width; 
                v = rowSelected;
                v = v / height;

                //v= Random.value*0.5f + 0.5f;

                // Store 2D coords
                /*
                color.r =  u;
                color.g =  v;
                */

                //Store 3D coords
                float phi   = -u*Mathf.PI + Mathf.PI/2.0f;
                float theta = Mathf.PI * (1.0f-v);
                color.r = (1.0f + (Mathf.Sin(theta) * Mathf.Sin(phi))) / 2.0f;
                color.g = (1.0f + Mathf.Cos(theta) ) / 2.0f;
                color.b = (1.0f + Mathf.Sin(theta) * Mathf.Cos(phi)) / 2.0f;
                
            //directionEnv = normalize(directionEnv);
            
            envSamplingTexture.SetPixel(i, j, color);

            }
        }
        envSamplingTexture.Apply();

        //Debug.Log(UnityEditor.AssetDatabase.GetAssetPath(envSamplingTexture));
        /*

         byte[] bytes = envSamplingTexture.EncodeToPNG();
         var dirPath = Application.dataPath + "/Resources/Textures";
         if (!System.IO.Directory.Exists(dirPath))
         {
             System.IO.Directory.CreateDirectory(dirPath);
         }
         System.IO.File.WriteAllBytes(dirPath + "/envSamplingTexture_" + Random.Range(0, 100000) + ".png", bytes);
         Debug.Log(bytes.Length / 1024 + "Kb was saved as: " + dirPath);
 #if UNITY_EDITOR
         UnityEditor.AssetDatabase.Refresh();
 #endif
 */

    }
}
