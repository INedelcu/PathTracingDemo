using System;
using UnityEngine;
using UnityStandardAssets.ImageEffects;

[Serializable]
[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
[AddComponentMenu("Image Effects/Contrast Enhance (Unsharp Mask)")]
public class ContrastEnhance : PostEffectsBase
{
    public float intensity;
    public float threshhold;
    private Material separableBlurMaterial;
    private Material contrastCompositeMaterial;
    public float blurSpread;
    public Shader separableBlurShader;
    public Shader contrastCompositeShader;

    public override bool CheckResources()
    {
        CheckSupport(false);
        contrastCompositeMaterial = CheckShaderAndCreateMaterial(contrastCompositeShader, contrastCompositeMaterial);
        separableBlurMaterial = CheckShaderAndCreateMaterial(separableBlurShader, separableBlurMaterial);
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
        var halfRezColor = RenderTexture.GetTemporary((int)(source.width / 2f), (int)(source.height / 2f), 0);
        var quarterRezColor = RenderTexture.GetTemporary((int)(source.width / 4f), (int)(source.height / 4f), 0);
        var secondQuarterRezColor = RenderTexture.GetTemporary((int)(source.width / 4f), (int)(source.height / 4f), 0);
        // ddownsample
        Graphics.Blit(source, halfRezColor);
        Graphics.Blit(halfRezColor, quarterRezColor);
        // blur
        separableBlurMaterial.SetVector("offsets", new Vector4(0f, (blurSpread * 1f) / quarterRezColor.height, 0f, 0f));
        Graphics.Blit(quarterRezColor, secondQuarterRezColor, separableBlurMaterial);
        separableBlurMaterial.SetVector("offsets", new Vector4((blurSpread * 1f) / quarterRezColor.width, 0f, 0f, 0f));
        Graphics.Blit(secondQuarterRezColor, quarterRezColor, separableBlurMaterial);
        // composite
        contrastCompositeMaterial.SetTexture("_MainTexBlurred", quarterRezColor);
        contrastCompositeMaterial.SetFloat("intensity", intensity);
        contrastCompositeMaterial.SetFloat("threshhold", threshhold);
        Graphics.Blit(source, destination, contrastCompositeMaterial);
        RenderTexture.ReleaseTemporary(halfRezColor);
        RenderTexture.ReleaseTemporary(quarterRezColor);
        RenderTexture.ReleaseTemporary(secondQuarterRezColor);
    }

    public ContrastEnhance()
    {
        intensity = 0.5f;
        blurSpread = 1f;
    }
}
