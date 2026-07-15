#if UNITY_EDITOR

using System;

using UnityEngine;
using UnityEditor;

public class PathTracingSimpleShaderGUI : ShaderGUI
{
    private enum SurfaceType
    {
        Opaque,
        Cutout
    }
    private static class Styles
    {
        public static GUIContent albedoText = EditorGUIUtility.TrTextContent("Albedo", "Albedo (RGB)");
        public static GUIContent emissionText = EditorGUIUtility.TrTextContent("Emission", "Emission (RGB)");
        public static GUIContent normalMapText = EditorGUIUtility.TrTextContent("Normal Map", "Tangent space normal map");
        public static GUIContent surfaceMode = EditorGUIUtility.TrTextContent("Surface Type", "Opaque or Cutout");
        public static GUIContent doubleSidedText = EditorGUIUtility.TrTextContent("Double Sided", "Render and trace both front and back faces");
        public static GUIContent energyCompText = EditorGUIUtility.TrTextContent("Energy Compensation", "Add back the energy single-scatter GGX loses at high roughness (Kulla-Conty multiple scattering). Matters most for rough metals.");
        public static readonly string[] surfaceTypeNames = Enum.GetNames(typeof(SurfaceType));
    }

    MaterialEditor m_MaterialEditor;

    MaterialProperty albedoTex = null;
    MaterialProperty albedoColor = null;
    MaterialProperty alphaCutoff = null;
    MaterialProperty metalicValue = null;
    MaterialProperty emissionTex = null;
    MaterialProperty emissionColor = null;
    MaterialProperty normalMapTex = null;
    MaterialProperty normalMapScale = null;
    MaterialProperty specularColor = null;
    MaterialProperty smoothnessValue = null;
    MaterialProperty iorValue = null;
    MaterialProperty surfaceType = null;
    MaterialProperty cullMode = null;
    MaterialProperty energyCompensation = null;

    bool firstTimeApply = true;

    public void FindProperties(MaterialProperty[] props)
    {
        surfaceType = FindProperty("_SurfaceType", props);
        cullMode = FindProperty("_Cull", props);
        energyCompensation = FindProperty("_EnergyCompensation", props);

        albedoTex = FindProperty("_MainTex", props);
        albedoColor = FindProperty("_Color", props);
        alphaCutoff = FindProperty("_Cutoff", props);

        metalicValue = FindProperty("_Metallic", props);

        emissionTex = FindProperty("_EmissionTex", props);
        emissionColor = FindProperty("_EmissionColor", props);

        normalMapTex = FindProperty("_NormalMapTex", props);
        normalMapScale = FindProperty("_NormalMapScale", props);

        specularColor = FindProperty("_SpecularColor", props);

        smoothnessValue = FindProperty("_Smoothness", props);

        iorValue = FindProperty("_IOR", props);
    }

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        FindProperties(properties);

        m_MaterialEditor = materialEditor;

        Material material = materialEditor.target as Material;

        if (firstTimeApply)
        {
            MaterialChanged(material);
            firstTimeApply = false;
        }

        ShaderPropertiesGUI(material);
    }

    static void SetKeyword(Material m, string keyword, bool state)
    {
        if (state)
            m.EnableKeyword(keyword);
        else
            m.DisableKeyword(keyword);
    }

    void SetMaterialKeywords(Material m)
    {
        SetKeyword(m, "EMISSION_ON", m.GetColor("_EmissionColor").maxColorComponent > 0.0f);
        SetKeyword(m, "NORMAL_MAP_ON", m.GetTexture("_NormalMapTex") != null);
        SetKeyword(m ,"DOUBLE_SIDED_ON", (cullMode.floatValue == (float)UnityEngine.Rendering.CullMode.Off));
        SetKeyword(m, "ENERGY_COMPENSATION_ON", energyCompensation.floatValue > 0.5f);

        var surfaceTypeValue = (SurfaceType)surfaceType.intValue;

        switch (surfaceTypeValue)
        {
            case SurfaceType.Opaque:
                m.SetOverrideTag("RenderType", "");
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                m.SetFloat("_ZWrite", 1.0f);
                SetKeyword(m, "ALPHATEST_ON", false);
                m.renderQueue = -1;
                break;
            case SurfaceType.Cutout:
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                m.SetFloat("_ZWrite", 1.0f);
                SetKeyword(m, "ALPHATEST_ON", true);
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                break;
        }
    }

    void MaterialChanged(Material material)
    {
        SetMaterialKeywords(material);
    }

    public void ShaderPropertiesGUI(Material material)
    {
        EditorGUIUtility.labelWidth = 0f;

        var surfaceTypeValue = (SurfaceType)surfaceType.intValue;
        var doubleSided = cullMode.floatValue == (float)UnityEngine.Rendering.CullMode.Off;
        var energyComp = energyCompensation.floatValue > 0.5f;

        EditorGUI.BeginChangeCheck();
        {
            surfaceTypeValue = (SurfaceType)EditorGUILayout.Popup(Styles.surfaceMode, (int)surfaceTypeValue, Styles.surfaceTypeNames);

            if (surfaceTypeValue == SurfaceType.Cutout)
            {
                EditorGUI.indentLevel = 1;
                m_MaterialEditor.RangeProperty(alphaCutoff, "Alpha Cutoff");
                EditorGUI.indentLevel = 0;
            }

            doubleSided = EditorGUILayout.Toggle(Styles.doubleSidedText, doubleSided);

            m_MaterialEditor.TexturePropertySingleLine(Styles.albedoText, albedoTex, albedoColor);

            EditorGUI.indentLevel = 1;
            m_MaterialEditor.TextureScaleOffsetProperty(albedoTex);
            EditorGUI.indentLevel = 0;

            m_MaterialEditor.TexturePropertySingleLine(Styles.normalMapText, normalMapTex, normalMapTex.textureValue != null ? normalMapScale : null);

            if (normalMapTex.textureValue != null)
            {
                EditorGUI.indentLevel = 1;

                m_MaterialEditor.TextureScaleOffsetProperty(normalMapTex);

                EditorGUI.indentLevel = 0;
            }

            m_MaterialEditor.ColorProperty(specularColor, "Specular Color");
            m_MaterialEditor.RangeProperty(metalicValue, "Metallic");
            m_MaterialEditor.RangeProperty(smoothnessValue, "Smoothness");
            EditorGUI.indentLevel = 1;
            energyComp = EditorGUILayout.Toggle(Styles.energyCompText, energyComp);
            EditorGUI.indentLevel = 0;
            m_MaterialEditor.RangeProperty(iorValue, "Index Of Refraction");

            bool hadEmissionTexture = emissionTex.textureValue != null;

            m_MaterialEditor.TexturePropertyWithHDRColor(Styles.emissionText, emissionTex, emissionColor, false);

            if (emissionTex.textureValue != null)
            {
                EditorGUI.indentLevel = 1;

                m_MaterialEditor.TextureScaleOffsetProperty(emissionTex);

                EditorGUI.indentLevel = 0;

                if (!hadEmissionTexture && emissionColor.colorValue.maxColorComponent <= 0f)
                    emissionColor.colorValue = Color.white;
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            surfaceType.intValue = (int)surfaceTypeValue;

            cullMode.floatValue = (float)(doubleSided ? UnityEngine.Rendering.CullMode.Off : UnityEngine.Rendering.CullMode.Back);

            energyCompensation.floatValue = energyComp ? 1.0f : 0.0f;

            MaterialChanged(material);
        }
    }
}

#endif