using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 货架预制体工具（只做两件事）。
///
/// 原则：绝不改动货架原有的位置 / 旋转 / 尺寸。
/// 只在货架根下加一个空物体 Parcels 当作「货物容器」，
/// 并给它做反向缩放补偿（内部世界缩放恒为 1），
/// 这样往里面摆快递不会被货板的非均匀缩放压扁。
///
///   Rack            ← 原有的一切都不动
///   ├─ Ban1 / Ban2  ← 货板，不动
///   └─ Parcels      ← 新增的空物体，货物挂这里
///
/// 用法（Hierarchy 里先选中货架根）：
///   货架 1 → 货架 2
/// </summary>
public static class RackPrefabTool
{
    private const string MenuRoot = "Tools/ExpressWar/";
    private const string PrefabFolder = "Assets/Perfabs";

    // ---------- 1. 生成货物容器 ----------
    [MenuItem(MenuRoot + "货架 1 · 生成货物容器（不改尺寸）", false, 1)]
    private static void SetupParcelRoots()
    {
        GameObject root = Selection.activeGameObject;
        if (root == null || root.GetComponentInChildren<RackController>(true) == null)
        {
            EditorUtility.DisplayDialog("生成货物容器",
                "请先在 Hierarchy 里选中货架根物体（它下面要有 RackController）。", "好");
            return;
        }

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("生成货物容器");

        Transform rootT = root.transform;

        // 只新增一个空物体，并把它的缩放反向补偿掉（内部世界缩放 = 1）
        Transform parcels = GetOrCreateChild(rootT, "Parcels");
        Undo.RecordObject(parcels, "补偿缩放");
        parcels.localScale = InverseScale(rootT.lossyScale);

        HashSet<string> usedNames = new HashSet<string>();

        foreach (RackController rack in root.GetComponentsInChildren<RackController>(true))
        {
            // 每块货板一个独立容器，共用会互相串台
            Transform parcelSlot = GetOrCreateUniqueChild(parcels, rack.gameObject.name, usedNames);

            Undo.RecordObject(rack, "配置货架");
            rack.ParcelRoot = parcelSlot;
            EditorUtility.SetDirty(rack);
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(root.scene);

        Debug.Log($"[Rack] 已生成货物容器：{root.name}/Parcels。" +
                  $"货架的位置、旋转、尺寸都没有任何改动。", root);
    }

    // ---------- 2. 保存预制体 ----------
    [MenuItem(MenuRoot + "货架 2 · 保存选中为预制体", false, 2)]
    private static void SaveAsPrefab()
    {
        GameObject root = Selection.activeGameObject;
        if (root == null)
        {
            EditorUtility.DisplayDialog("保存预制体", "请先在 Hierarchy 里选中货架根物体。", "好");
            return;
        }

        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets", "Perfabs");

        string path = AssetDatabase.GenerateUniqueAssetPath($"{PrefabFolder}/{root.name}.prefab");
        PrefabUtility.SaveAsPrefabAssetAndConnect(root, path, InteractionMode.UserAction);
        AssetDatabase.SaveAssets();

        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        Debug.Log($"[Rack] 预制体已保存：{path}", root);
    }

    // ---------- 工具 ----------
    private static Transform GetOrCreateChild(Transform parent, string childName)
    {
        Transform found = parent.Find(childName);
        if (found != null) return found;

        GameObject go = new GameObject(childName);
        Undo.RegisterCreatedObjectUndo(go, "创建 " + childName);

        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        return go.transform;
    }

    // 同名货板自动加序号，避免两个货架共用同一个节点
    private static Transform GetOrCreateUniqueChild(Transform parent, string baseName, HashSet<string> used)
    {
        string childName = baseName;
        int index = 1;

        while (used.Contains(childName))
            childName = $"{baseName} ({index++})";

        used.Add(childName);
        return GetOrCreateChild(parent, childName);
    }

    /// <summary>反向缩放：让子物体的世界缩放回到 1（父级有非均匀缩放时也不会压扁货物）</summary>
    private static Vector3 InverseScale(Vector3 scale)
    {
        return new Vector3(
            Mathf.Abs(scale.x) > 0.0001f ? 1f / scale.x : 1f,
            Mathf.Abs(scale.y) > 0.0001f ? 1f / scale.y : 1f,
            Mathf.Abs(scale.z) > 0.0001f ? 1f / scale.z : 1f);
    }
}
