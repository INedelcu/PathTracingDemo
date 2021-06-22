using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public class AdditionalCameraData : MonoBehaviour
{
    [HideInInspector]
    public Matrix4x4 previousViewProjection;

    [HideInInspector]
    public int frameIndex;

    [HideInInspector]
    public RenderTexture colorHistory = null;

    [HideInInspector]
    public RenderTexture gBufferWorldNormals = null;

    [HideInInspector]
    public RenderTexture gBufferIntersectionT = null;

    [HideInInspector]
    public RenderTexture gBufferMotionVectors = null;

    [HideInInspector]
    public RenderTexture rayTracingOutput = null;

    [HideInInspector]
    public RenderTexture aTrousPingpongRadiance = null;

    [HideInInspector]
    public RenderTexture aTrousVariance = null;

    [HideInInspector]
    public RenderTexture aTrousPingpongVariance = null;

    // Start is called before the first frame update
    void Start()
    {
        frameIndex = 0;
        previousViewProjection = Matrix4x4.identity;
    }

    // Update is called once per frame
    void Update()
    {

    }

    public void UpdateCameraDataPostRender(Camera camera)
    {
        previousViewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
        frameIndex++;
    }

    public void CreatePersistentResources(Camera camera)
    {

        if (colorHistory == null || colorHistory.width != camera.pixelWidth || colorHistory.height != camera.pixelHeight)
        {
            if (colorHistory != null)
                colorHistory.Release();

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

            colorHistory = new RenderTexture(rtDesc);
            colorHistory.Create();

            // when we (re)create the history buffer, reset the iteration for the camera
            frameIndex = 0;
        }

        if (rayTracingOutput == null || rayTracingOutput.width != camera.pixelWidth || rayTracingOutput.height != camera.pixelHeight)
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

        if (aTrousPingpongRadiance == null || aTrousPingpongRadiance.width != camera.pixelWidth || aTrousPingpongRadiance.height != camera.pixelHeight)
        {
            if (aTrousPingpongRadiance)
                aTrousPingpongRadiance.Release();

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

            aTrousPingpongRadiance = new RenderTexture(rtDesc);
            aTrousPingpongRadiance.Create();
        }

        if (aTrousVariance == null || aTrousVariance.width != camera.pixelWidth || aTrousVariance.height != camera.pixelHeight)
        {
            if (aTrousVariance)
                aTrousVariance.Release();

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

            aTrousVariance = new RenderTexture(rtDesc);
            aTrousVariance.Create();
        }

        if (aTrousPingpongVariance == null || aTrousPingpongVariance.width != camera.pixelWidth || aTrousPingpongVariance.height != camera.pixelHeight)
        {
            if (aTrousPingpongVariance)
                aTrousPingpongVariance.Release();

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

            aTrousPingpongVariance = new RenderTexture(rtDesc);
            aTrousPingpongVariance.Create();
        }        

        if (gBufferWorldNormals == null || gBufferWorldNormals.width != camera.pixelWidth || gBufferWorldNormals.height != camera.pixelHeight)
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


        if (gBufferIntersectionT == null || gBufferIntersectionT.width != camera.pixelWidth || gBufferIntersectionT.height != camera.pixelHeight)
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

        if (gBufferMotionVectors == null || gBufferMotionVectors.width != camera.pixelWidth || gBufferMotionVectors.height != camera.pixelHeight)
        {
            if (gBufferMotionVectors)
                gBufferMotionVectors.Release();

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

            gBufferMotionVectors = new RenderTexture(rtDesc);
            gBufferMotionVectors.Create();
        }
    }

    void OnDestroy()
    {
        if (colorHistory != null)
        {
            colorHistory.Release();
            colorHistory = null;
        }

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

        if (gBufferMotionVectors != null)
        {
            gBufferMotionVectors.Release();
            gBufferMotionVectors = null;
        }
    }
}
