using System;
using UnityEngine;
using UnityStandardAssets.ImageEffects;

[Serializable]
[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
[AddComponentMenu("Image Effects/Crease")]
public class Crease : PostEffectsBase
{
    public float intensity;
    public int softness;
    public float spread;
    public Shader blurShader;
    private Material blurMaterial;
    public Shader depthFetchShader;
    private Material depthFetchMaterial;
    public Shader creaseApplyShader;
    private Material creaseApplyMaterial;

    public override bool CheckResources()
    {
        CheckSupport(true);
        blurMaterial = CheckShaderAndCreateMaterial(blurShader, blurMaterial);
        depthFetchMaterial = CheckShaderAndCreateMaterial(depthFetchShader, depthFetchMaterial);
        creaseApplyMaterial = CheckShaderAndCreateMaterial(creaseApplyShader, creaseApplyMaterial);
        if (!isSupported)
        {
            ReportAutoDisable();
        }
        return isSupported;
    }

    public virtual void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (CheckResources() == false)
        {
            Graphics.Blit(source, destination);
            return;
        }
        var widthOverHeight = (1f * source.width) / (1f * source.height);
        var oneOverBaseSize = 1f / 512f;
        var hrTex = RenderTexture.GetTemporary(source.width, source.height, 0);
        var lrTex1 = RenderTexture.GetTemporary(source.width / 2, source.height / 2, 0);
        var lrTex2 = RenderTexture.GetTemporary(source.width / 2, source.height / 2, 0);
        Graphics.Blit(source, hrTex, depthFetchMaterial);
        Graphics.Blit(hrTex, lrTex1);
        var i = 0;
        while (i < softness)
        {
            blurMaterial.SetVector("offsets", new Vector4(0f, spread * oneOverBaseSize, 0f, 0f));
            Graphics.Blit(lrTex1, lrTex2, blurMaterial);
            blurMaterial.SetVector("offsets", new Vector4((spread * oneOverBaseSize) / widthOverHeight, 0f, 0f, 0f));
            Graphics.Blit(lrTex2, lrTex1, blurMaterial);
            i++;
        }
        creaseApplyMaterial.SetTexture("_HrDepthTex", hrTex);
        creaseApplyMaterial.SetTexture("_LrDepthTex", lrTex1);
        creaseApplyMaterial.SetFloat("intensity", intensity);
        Graphics.Blit(source, destination, creaseApplyMaterial);
        RenderTexture.ReleaseTemporary(hrTex);
        RenderTexture.ReleaseTemporary(lrTex1);
        RenderTexture.ReleaseTemporary(lrTex2);
    }

    public Crease()
    {
        intensity = 0.5f;
        softness = 1;
        spread = 1f;
    }
}
