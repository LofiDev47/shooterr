using UnityEditor;
using UnityEngine;

public static class ShaderCompileCheck
{
    public static void Run()
    {
        string[] shaderNames = {
            "Shooter/Foundry Armor",
            "Shooter/Foundry Alloy",
            "Shooter/Foundry Glow",
            "Shooter/Foundry Surface",
            "Shooter/Foundry Signal"
        };
        int errors = 0;
        foreach (string name in shaderNames)
        {
            Shader s = Shader.Find(name);
            if (s == null)
            {
                Debug.LogError($"[ShaderCheck] Shader not found: {name}");
                errors++;
                continue;
            }
            Material m = new Material(s);
            if (ShaderUtil.ShaderHasError(s))
            {
                Debug.LogError($"[ShaderCheck] Compile error: {name}");
                errors++;
            }
            else
            {
                Debug.Log($"[ShaderCheck] OK: {name} ({ShaderUtil.GetPropertyCount(s)} properties)");
            }
            Object.DestroyImmediate(m);
        }

        // Check material assignments
        string[] matPaths = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Materials/Foundry" });
        int armor = 0, alloy = 0, glow = 0, signal = 0, surface = 0, urpLit = 0, other = 0;
        foreach (string guid in matPaths)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;
            switch (mat.shader.name)
            {
                case "Shooter/Foundry Armor": armor++; break;
                case "Shooter/Foundry Alloy": alloy++; break;
                case "Shooter/Foundry Glow": glow++; break;
                case "Shooter/Foundry Signal": signal++; break;
                case "Shooter/Foundry Surface": surface++; break;
                case "Universal Render Pipeline/Lit": urpLit++; break;
                default: other++; break;
            }
        }
        Debug.Log($"[ShaderCheck] Foundry materials: Armor={armor} Alloy={alloy} Glow={glow} Signal={signal} Surface={surface} URP_Lit={urpLit} Other={other}");

        if (errors > 0)
            Debug.LogError($"[ShaderCheck] {errors} shader(s) failed!");
        else
            Debug.Log("[ShaderCheck] All shaders compiled successfully!");
    }
}
