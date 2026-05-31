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
        public static GUIContent emissionText = EditorGUIUtility.TrTextContent("Color", "Emission (RGB)");
        public static GUIContent surfaceMode = EditorGUIUtility.TrTextContent("Surface Type", "Opaque or Cutout");
        public static GUIContent doubleSidedText = EditorGUIUtility.TrTextContent("Double Sided", "Render and trace both front and back faces");
        public static readonly string[] surfaceTypeNames = Enum.GetNames(typeof(SurfaceType));
    }

    MaterialEditor m_MaterialEditor;

    MaterialProperty albedoTex = null;
    MaterialProperty albedoColor = null;
    MaterialProperty alphaCutoff = null;
    MaterialProperty metalicValue = null;
    MaterialProperty emissionState = null;
    MaterialProperty emissionTex = null;
    MaterialProperty emissionColor = null;
    MaterialProperty specularColor = null;
    MaterialProperty smoothnessValue = null;
    MaterialProperty iorValue = null;
    MaterialProperty surfaceType = null;
    MaterialProperty cullMode = null;

    bool firstTimeApply = true;

    public void FindProperties(MaterialProperty[] props)
    {
        surfaceType = FindProperty("_SurfaceType", props);
        cullMode = FindProperty("_Cull", props);

        albedoTex = FindProperty("_MainTex", props);
        albedoColor = FindProperty("_Color", props);
        alphaCutoff = FindProperty("_Cutoff", props);

        metalicValue = FindProperty("_Metallic", props);

        emissionState = FindProperty("_Emission", props);
        emissionTex = FindProperty("_EmissionTex", props);
        emissionColor = FindProperty("_EmissionColor", props);

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
        SetKeyword(m, "EMISSION_ON", (emissionState.floatValue != 0.0f));
        SetKeyword(m ,"DOUBLE_SIDED_ON", (cullMode.floatValue == (float)UnityEngine.Rendering.CullMode.Off));

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

        var showEmissionSettings = false;
        var surfaceTypeValue = (SurfaceType)surfaceType.intValue;
        var doubleSided = cullMode.floatValue == (float)UnityEngine.Rendering.CullMode.Off;

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

            m_MaterialEditor.ColorProperty(specularColor, "Specular Color");
            m_MaterialEditor.RangeProperty(metalicValue, "Metallic");
            m_MaterialEditor.RangeProperty(smoothnessValue, "Smoothness");
            m_MaterialEditor.RangeProperty(iorValue, "Index Of Refraction");

            showEmissionSettings = (emissionState.floatValue != 0.0f);

            EditorGUI.showMixedValue = emissionState.hasMixedValue;

            showEmissionSettings = EditorGUILayout.Toggle("Emission", showEmissionSettings);

            EditorGUI.showMixedValue = false;

            if (showEmissionSettings)
            {
                m_MaterialEditor.TexturePropertyWithHDRColor(Styles.emissionText, emissionTex, emissionColor, false);

                EditorGUI.indentLevel = 1;

                m_MaterialEditor.TextureScaleOffsetProperty(emissionTex);

                EditorGUI.indentLevel = 0;

                bool hadEmissionTexture = emissionTex.textureValue != null;

                float brightness = emissionColor.colorValue.maxColorComponent;
                if (emissionTex.textureValue != null && !hadEmissionTexture && brightness <= 0f)
                    emissionColor.colorValue = Color.white;
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            emissionState.floatValue = showEmissionSettings ? 1.0f : 0.0f;

            surfaceType.intValue = (int)surfaceTypeValue;

            cullMode.floatValue = (float)(doubleSided ? UnityEngine.Rendering.CullMode.Off : UnityEngine.Rendering.CullMode.Back);

            MaterialChanged(material);
        }
    }
}

#endif