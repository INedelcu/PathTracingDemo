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
    }

    void OnDestroy()
    {
        if (colorHistory != null)
        {
            colorHistory.Release();
            colorHistory = null;
        }
    }
}
