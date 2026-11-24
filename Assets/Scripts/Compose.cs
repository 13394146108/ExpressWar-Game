using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 快递合成：货板上「首尾相接」的几件快递，只要占用格数加起来正好凑成 TargetUnits（默认 3 格），
/// 就合成一件大快递摆回原来的位置：
///   小 + 小 + 小 → 大
///   中 + 小      → 大
///   小 + 中      → 大
/// 中间隔了空档就不算（必须占的格子首尾相接），本来就是大快递的单件也不会再合成。
/// 只用 RackController 的公开接口（GetPlacedItems / RemovePlaced / PlaceAt / PlacedChanged），
/// 货架本身的摆放逻辑一点都不用改。
/// 用法：挂在一块货板上（和 RackController 同物体），或者挂在货架根上（自动管下面所有货板）。
/// </summary>
public class Compose : MonoBehaviour
{
    [Header("合成")]
    [Tooltip("合成出来的大快递预制体（要带 Rigidbody / CanGrab / GrabbableItem = Large）")]
    public GameObject LargeParcelPrefab;

    [Tooltip("凑够几个单位合成：默认 3 = 大快递，对应大快递的尺寸")]
    [Min(2)]
    public int TargetUnits = 3;

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
        // 预制体里预先摆好的组合也结算一次
        for (int i = 0; i < racks.Count; i++) ComposeAll(racks[i]);
    }

    // ---------- 合成 ----------
    private void OnRackChanged(RackController rack)
    {
        if (busy || rack == null) return;          // 合成过程中的变化不再重入
        ComposeAll(rack);
    }

    /// <summary>把这块货板上所有能凑成 TargetUnits 的组合都合成掉</summary>
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

    /// <summary>找第一段能凑成 TargetUnits 的快递并合成，成功返回 true</summary>
    private bool MergeOnce(RackController rack)
    {
        rack.GetPlacedItems(items);                // 已按起始格从左到右排好

        for (int start = 0; start < items.Count; start++)
        {
            int sum = 0;

            for (int end = start; end < items.Count; end++)
            {
                // 必须首尾相接：这一件正好接在上一件占的最后一格后面
                if (end > start && items[end].First != items[end - 1].Last + 1) break;

                sum += items[end].Units;
                if (sum > TargetUnits) break;      // 加超了，这段凑不出来

                if (sum != TargetUnits) continue;  // 还差几格，继续往后加

                if (end == start) break;           // 单件本来就够大，不用合成

                Merge(rack, start, end);
                return true;
            }
        }

        return false;
    }

    /// <summary>把 items[start..end] 这几件合成一件大快递，摆回它们原来占的那一段格子</summary>
    private void Merge(RackController rack, int start, int end)
    {
        RackController.PlacedInfo head = items[start];
        RackController.PlacedInfo tail = items[end];
        int firstSlot = head.First;

        // 生成位置先取这几件的中点，避免大快递在场景原点闪一帧
        Vector3 spawnPos = (head.Body.transform.position + tail.Body.transform.position) * 0.5f;

        // 1) 先把原来的快递从货架账本上摘下来（不动它们的物体）
        for (int i = start; i <= end; i++) rack.RemovePlaced(items[i].Body);

        // 2) 生成大快递，摆到这一段格子上
        Rigidbody merged = SpawnLarge(spawnPos, rack.transform.rotation);

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

    /// <summary>生成一件大快递（不摆位，摆位交给 RackController.PlaceAt）</summary>
    private Rigidbody SpawnLarge(Vector3 position, Quaternion rotation)
    {
        if (LargeParcelPrefab == null)
        {
            Debug.LogWarning("[Compose] 没有指定大快递预制体，合成已跳过", this);
            return null;
        }

        GameObject go = Instantiate(LargeParcelPrefab, position, rotation);

        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogWarning($"[Compose] 预制体 {LargeParcelPrefab.name} 上没有 Rigidbody，合成已跳过", this);
            Destroy(go);
            return null;
        }

        return rb;
    }
}
