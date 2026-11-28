using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 快递合成：货板上「首尾相接」的几件快递，只要占用格数加起来正好凑成目标格数：
///   小 + 小 + 小 → 大
///   中 + 小      → 大
///   小 + 中      → 大
///   小 + 小      → 中
/// 中间隔了空档就不算（必须占的格子首尾相接），本来就是目标尺寸的单件也不会再合成。
///
/// 优先匹配大快递配方，避免三个小快递先合成中快递、再合成大快递。
/// 合成结果直接使用对应预制体本身的颜色（默认颜色），不再做颜色混合。
///
/// 只用 RackController 的公开接口（GetPlacedItems / RemovePlaced / PlaceAt / PlacedChanged），
/// 货架本身的摆放逻辑一点都不用改。
/// 用法：挂在一块货板上（和 RackController 同物体），或者挂在货架根上（自动管下面所有货板）。
/// </summary>
public class Compose : MonoBehaviour
{
    [Header("合成")]
    [Tooltip("合成出来的大快递预制体（要带 Rigidbody / CanGrab / GrabbableItem = Large）")]
    public GameObject LargeParcelPrefab;

    [Tooltip("合成出来的中快递预制体（要带 Rigidbody / CanGrab / GrabbableItem = Medium）")]
    public GameObject MediumParcelPrefab;

    [Tooltip("大快递需要凑够的单位数：默认 3（小 1 / 中 2 / 大 3）")]
    [Min(2)]
    public int TargetUnits = 3;

    [Tooltip("中快递需要凑够的单位数：默认 2（两个小快递）")]
    [Min(2)]
    public int MediumTargetUnits = 2;

    // 自己 / 子节点上的所有货板（一个货架预制体里可能有多块板）
    private readonly List<RackController> racks = new List<RackController>();

    // 复用同一份列表，避免每次合成都分配
    private readonly List<RackController.PlacedInfo> items = new List<RackController.PlacedInfo>();

    // 合成过程中会再次触发 PlacedChanged，用它挡住递归
    private bool busy;

    // ---------- 生命周期 ----------
    void Awake()
    {
        racks.Clear();
        racks.AddRange(GetComponentsInChildren<RackController>(true));   // 含未激活的子物体

        if (racks.Count == 0)
        {
            RackController parent = GetComponentInParent<RackController>();
            if (parent != null) racks.Add(parent);
        }
    }

    void OnEnable()
    {
        for (int i = 0; i < racks.Count; i++)
            if (racks[i] != null) racks[i].PlacedChanged += OnRackChanged;
    }

    void OnDisable()
    {
        for (int i = 0; i < racks.Count; i++)
            if (racks[i] != null) racks[i].PlacedChanged -= OnRackChanged;
    }

    void Start()
    {
        if (MediumParcelPrefab == null)
            Debug.LogWarning("[Compose] 没有指定中快递预制体，两个小快递不会合成中快递", this);

        // 预制体里预先摆好的组合也结算一次
        for (int i = 0; i < racks.Count; i++) ComposeAll(racks[i]);
    }

    // ---------- 合成 ----------
    private void OnRackChanged(RackController rack)
    {
        if (busy || rack == null) return;          // 合成过程中的变化不再重入
        ComposeAll(rack);
    }

    /// <summary>把这块货板上所有能凑成目标格数的组合都合成掉</summary>
    private void ComposeAll(RackController rack)
    {
        if (rack == null) return;

        busy = true;
        try
        {
            int guard = 32;                        // 兜底：防止意外死循环
            while (guard-- > 0 && MergeOnce(rack)) { }
        }
        finally
        {
            busy = false;
        }
    }

    /// <summary>先找大快递配方，再找中快递配方；找到一组就合成并返回 true</summary>
    private bool MergeOnce(RackController rack)
    {
        rack.GetPlacedItems(items);                // 已按起始格从左到右排好

        if (TryMergeTarget(rack, TargetUnits, LargeParcelPrefab)) return true;
        if (TryMergeTarget(rack, MediumTargetUnits, MediumParcelPrefab)) return true;

        return false;
    }

    /// <summary>在连续摆放的快递里找第一段总和正好等于 targetUnits 的组合</summary>
    private bool TryMergeTarget(RackController rack, int targetUnits, GameObject prefab)
    {
        if (prefab == null || targetUnits < 2) return false;

        for (int start = 0; start < items.Count; start++)
        {
            int sum = 0;

            for (int end = start; end < items.Count; end++)
            {
                // 必须首尾相接：这一件正好接在上一件占的最后一格后面
                if (end > start && items[end].First != items[end - 1].Last + 1) break;

                sum += items[end].Units;
                if (sum > targetUnits) break;      // 加超了，这段凑不出来

                if (sum != targetUnits) continue;  // 还差几格，继续往后加

                if (end == start) break;           // 单件本来就够大，不用合成

                Merge(rack, start, end, prefab);
                return true;
            }
        }

        return false;
    }

    /// <summary>把 items[start..end] 合成一件指定预制体，摆回它们原来占的那一段格子</summary>
    private void Merge(RackController rack, int start, int end, GameObject prefab)
    {
        RackController.PlacedInfo head = items[start];
        RackController.PlacedInfo tail = items[end];
        int firstSlot = head.First;

        // 生成位置先取这几件的中点，避免合成物在场景原点闪一帧
        Vector3 spawnPos = (head.Body.transform.position + tail.Body.transform.position) * 0.5f;

        // 1) 先把原来的快递从货架账本上摘下来（不动它们的物体）
        for (int i = start; i <= end; i++) rack.RemovePlaced(items[i].Body);

        // 2) 生成合成结果，摆到这一段格子上
        Rigidbody merged = Spawn(prefab, spawnPos, rack.transform.rotation);

        if (merged == null || !rack.PlaceAt(merged, firstSlot))
        {
            // 合成失败：把原来的快递原样放回去，别让东西凭空消失
            if (merged != null) Destroy(merged.gameObject);

            for (int i = start; i <= end; i++) rack.PlaceAt(items[i].Body, items[i].First);
            return;
        }

        // 3) 原来的几件已经被合成了，销毁
        for (int i = start; i <= end; i++)
        {
            Rigidbody body = items[i].Body;
            if (body != null) Destroy(body.gameObject);
        }
    }

    /// <summary>生成一件合成结果（不摆位，摆位交给 RackController.PlaceAt）</summary>
    private Rigidbody Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null)
        {
            Debug.LogWarning("[Compose] 没有指定合成结果预制体，合成已跳过", this);
            return null;
        }

        GameObject go = Instantiate(prefab, position, rotation);

        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogWarning($"[Compose] 预制体 {prefab.name} 上没有 Rigidbody，合成已跳过", this);
            Destroy(go);
            return null;
        }

        return rb;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // 没有手动指定时，按尺寸从 Assets/Perfabs 里自动找中/大预制体
        if (MediumParcelPrefab == null)
            MediumParcelPrefab = FindPrefabBySize(GrabbableItem.ItemSize.Medium);

        if (LargeParcelPrefab == null)
            LargeParcelPrefab = FindPrefabBySize(GrabbableItem.ItemSize.Large);
    }

    private static GameObject FindPrefabBySize(GrabbableItem.ItemSize size)
    {
        string[] guids = UnityEditor.AssetDatabase.FindAssets(
            "t:Prefab", new[] { "Assets/Perfabs" });

        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            GrabbableItem item = prefab.GetComponent<GrabbableItem>();
            if (item == null) item = prefab.GetComponentInChildren<GrabbableItem>(true);

            if (item != null && item.Size == size) return prefab;
        }

        return null;
    }
#endif
}
