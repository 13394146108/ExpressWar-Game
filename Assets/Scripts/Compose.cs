using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 快递合成：货板上「首尾相接」的几件快递，只要占用格数加起来正好凑成 TargetUnits（默认 3 格），
/// 就合成一件大快递摆回原来的位置：
///   小 + 小 + 小 → 大
///   中 + 小      → 大
///   小 + 中      → 大
/// 中间隔了空档就不算（必须占的格子首尾相接），本来就是大快递的单件也不会再合成。
///
/// 颜色也一起合：按参与合成的快递里出现过的颜色（黄 = 普通 / 红 = 易爆 / 蓝 = 易碎）决定结果：
///   只有一种颜色 → 保持那种颜色（黄还是黄，红还是红，蓝还是蓝）
///   黄 + 红 → Y+R，黄 + 蓝 → Y+B，红 + 蓝 → R+B，黄 + 红 + 蓝 → Y+R+B
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

    [Tooltip("凑够几个单位合成：默认 3 = 大快递，对应大快递的尺寸")]
    [Min(2)]
    public int TargetUnits = 3;

    [Header("混色材质（只用于两种以上颜色合成，单色沿用原色）")]
    [Tooltip("黄 + 红：Assets/Materials/Y+R")]
    public Material YellowRed;

    [Tooltip("黄 + 蓝：Assets/Materials/Y+B")]
    public Material YellowBlue;

    [Tooltip("红 + 蓝：Assets/Materials/R+B")]
    public Material RedBlue;

    [Tooltip("黄 + 红 + 蓝：Assets/Materials/Y+R+B")]
    public Material YellowRedBlue;

    // 颜色位：和 ParcelType 的枚举值一一对应（把数字1的二进制向左移n位）
    private const int ColorYellow = 1 << (int)ParcelType.Normal;      // 黄（普通）0    1（0001）
    private const int ColorBlue = 1 << (int)ParcelType.Fragile;       // 蓝（易碎）1    2（0010）
    private const int ColorRed = 1 << (int)ParcelType.Explosive;      // 红（易爆）2    4（0100）

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

        // 合出来的颜色由这批快递里出现过的颜色决定（要在销毁原件之前取）
        int colorMask = ColorMaskOf(start, end);

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

        // 3) 刷成合成后的颜色
        ApplyMergedColor(merged, colorMask);

        // 4) 原来的几件已经被合成了，销毁
        for (int i = start; i <= end; i++)
        {
            Rigidbody body = items[i].Body;
            if (body != null) Destroy(body.gameObject);
        }
    }

    // ---------- 颜色 ----------
    /// <summary>这一批快递里出现过哪些颜色（位掩码）</summary>
    private int ColorMaskOf(int start, int end)
    {
        int mask = 0;

        for (int i = start; i <= end; i++)
            mask |= 1 << (int)TypeOf(items[i].Body);

        return mask;
    }

    /// <summary>快递当前的颜色类型（没挂 ParcelFeature 时按普通/黄色算）</summary>
    private static ParcelType TypeOf(Rigidbody rb)
    {
        if (rb == null) return ParcelType.Normal;

        ParcelFeature feature = rb.GetComponentInChildren<ParcelFeature>(true);
        return feature != null ? feature.Type : ParcelType.Normal;
    }

    /// <summary>
    /// 把合成结果刷成对应的颜色：
    /// 只有一种颜色 → 保持原色；两种以上 → 换成对应的混色材质。
    /// </summary>
    private void ApplyMergedColor(Rigidbody merged, int colorMask)
    {
        ParcelFeature feature = merged.GetComponentInChildren<ParcelFeature>(true);
        if (feature == null) feature = merged.gameObject.AddComponent<ParcelFeature>();

        // 只有一种颜色：沿用那种颜色的原有表现（黄=默认材质，蓝/红=颜色覆盖）
        if ((colorMask & (colorMask - 1)) == 0)
        {
            feature.SetType(ToType(colorMask));
            return;
        }

        // 混色：先回到普通，把单色覆盖清掉，再整体换材质
        feature.SetType(ParcelType.Normal);

        Material mixed = MixedMaterial(colorMask);
        if (mixed == null)
        {
            Debug.LogWarning("[Compose] 混色材质没配全，这件合成快递先按普通颜色显示", this);
            return;
        }

        foreach (Renderer r in merged.GetComponentsInChildren<Renderer>(true))
            if (r != null) r.sharedMaterial = mixed;
    }

    /// <summary>单色掩码 → 对应的 ParcelType</summary>
    private static ParcelType ToType(int singleColorMask)
    {
        if (singleColorMask == ColorYellow) return ParcelType.Normal;
        if (singleColorMask == ColorBlue) return ParcelType.Fragile;

        return ParcelType.Explosive;      // 只剩红
    }

    /// <summary>多色掩码 → 混色材质（单色返回 null）</summary>
    private Material MixedMaterial(int colorMask)
    {
        switch (colorMask)
        {
            case ColorYellow | ColorRed: return YellowRed;
            case ColorYellow | ColorBlue: return YellowBlue;
            case ColorRed | ColorBlue: return RedBlue;
            case ColorYellow | ColorRed | ColorBlue: return YellowRedBlue;
            default: return null;
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

#if UNITY_EDITOR
    // ---------- 编辑器便利 ----------
    void OnValidate()
    {
        // 手没拖的话，按名字把工程里已经建好的混色材质找出来（Assets/Materials/Y+R 等）
        if (YellowRed == null) YellowRed = FindMaterialByName("Y+R");
        if (YellowBlue == null) YellowBlue = FindMaterialByName("Y+B");
        if (RedBlue == null) RedBlue = FindMaterialByName("R+B");
        if (YellowRedBlue == null) YellowRedBlue = FindMaterialByName("Y+R+B");
    }

    private static Material FindMaterialByName(string materialName)
    {
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Material");

        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) != materialName) continue;

            return UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        return null;
    }
#endif
}
