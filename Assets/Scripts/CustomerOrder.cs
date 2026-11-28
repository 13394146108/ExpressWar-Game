using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>顾客想要的快递尺寸</summary>
public enum OrderSize
{
    Small,
    Medium,
    Large
}

/// <summary>顾客订单：尺寸 + 最终颜色（RGB）</summary>
[System.Serializable]
public struct ParcelOrder
{
    public OrderSize Size;
    public Color Color;

    public ParcelOrder(OrderSize size, Color color)
    {
        Size = size;
        Color = color;
    }

    /// <summary>调试用文字，例如 "Large #5500AA"</summary>
    public string DisplayText
    {
        get
        {
            string sizeText;
            if (Size == OrderSize.Small) sizeText = "Small";
            else if (Size == OrderSize.Medium) sizeText = "Medium";
            else sizeText = "Large";

            return sizeText + " #" + ColorUtility.ToHtmlStringRGB(Color);
        }
    }

    /// <summary>颜色是否一致（按 8 位 RGB 比较，忽略 alpha）</summary>
    public bool SameColor(Color other)
    {
        Color32 a = Color;
        Color32 b = other;
        return a.r == b.r && a.g == b.g && a.b == b.b;
    }
}

/// <summary>
/// 顾客需求：
///   1. 从 ParcelColorMixes.json 生成所有可能的「尺寸 + 颜色」订单；
///   2. 头顶不再显示文字，而是在 Give 桌面上生成半透明预览；
///   3. 交付时由这里判定尺寸和颜色是否匹配，并把结果交给耐心条。
///
/// 挂在 customer 上；Give 桌子留空会自动查找名为 Give 的物体。
/// </summary>
[DisallowMultipleComponent]
public class CustomerOrder : MonoBehaviour
{
    [Header("显示")]
    [Tooltip("顾客头顶的文字；留空时自动在子物体里找 TMP_Text")]
    public TMP_Text Label;

    [Tooltip("关闭后不再显示头顶文字，只显示桌面上的透明预览")]
    public bool ShowLabel = false;

    [Tooltip("开局随机一个订单")]
    public bool RandomizeOnStart = true;

    [Header("订单颜色")]
    [Tooltip("颜色混合表；留空时自动读取 Resources/ParcelColorMixes.json")]
    public TextAsset ColorMixJson;

    [Header("桌面预览")]
    [Tooltip("预览出现的位置；留空自动查找名为 Give 的物体")]
    public Transform GiveTable;

    [Tooltip("透明方块的不透明度：0 = 全透明，1 = 不透明；稍微透明建议 0.6 ~ 0.8")]
    [Range(0.05f, 0.95f)]
    public float GhostAlpha = 0.65f;

    [Tooltip("描边粗细（世界单位）")]
    [Min(0.001f)]
    public float GhostOutlineWidth = 0.015f;

    [Header("运行时（只读）")]
    [Tooltip("当前订单")]
    [System.NonSerialized] public ParcelOrder CurrentOrder;

    /// <summary>订单变化（生成新需求时触发），耐心条和预览都用它刷新</summary>
    public event System.Action<ParcelOrder> OrderChanged;

    /// <summary>每次换需求自增，用来判断需求是否已经被别人换过</summary>
    public int Version { get; private set; }

    private const string DefaultColorMixResource = "ParcelColorMixes";

    private readonly List<ParcelOrder> allOrders = new List<ParcelOrder>();
    private OrderGhostPreview ghost;
    private CustomerPatience patience;
    private bool ghostVisible = true;

    [System.Serializable]
    public class ColorMixTable
    {
        public ColorMixEntry[] mixes;
    }

    [System.Serializable]
    public class ColorMixEntry
    {
        public string id;
        public string[] colors;
        public int[] sizes;
        public ColorRgb result;
    }

    [System.Serializable]
    public class ColorRgb
    {
        public int r;
        public int g;
        public int b;
    }

    void Awake()
    {
        if (Label == null) Label = GetComponentInChildren<TMP_Text>(true);
        HideLabelIfNeeded();

        patience = GetComponent<CustomerPatience>();
        LoadOrders();
    }

    void Start()
    {
        if (RandomizeOnStart) RollNewOrder();
    }

    void OnEnable()
    {
        if (Version > 0) RefreshGhost();
    }

    void OnDisable()
    {
        if (ghost != null) ghost.Hide();
    }

    void OnDestroy()
    {
        if (ghost != null) ghost.Clear();
    }

    /// <summary>随机一个新订单，并刷新预览和耐心条</summary>
    public void RollNewOrder()
    {
        if (allOrders.Count == 0) LoadOrders();

        if (allOrders.Count == 0)
        {
            Debug.LogWarning("[CustomerOrder] 没有可用订单，请检查颜色混合 JSON", this);
            return;
        }

        SetOrder(allOrders[Random.Range(0, allOrders.Count)]);
    }

    /// <summary>指定订单，并刷新预览和耐心条</summary>
    public void SetOrder(ParcelOrder order)
    {
        CurrentOrder = order;
        Version++;

        RefreshLabel();
        RefreshGhost();
        OrderChanged?.Invoke(order);
    }

    /// <summary>由 GiveControl 调用：桌上有快递时隐藏预览，取走后再显示</summary>
    public void SetGhostVisible(bool visible)
    {
        ghostVisible = visible;
        RefreshGhost();
    }

    /// <summary>
    /// 由 GiveControl 在交付时调用：判定这件快递是否匹配当前订单，
    /// 并把正确 / 错误结果交给耐心条（错误扣一格，正确后换新订单回满）。
    /// </summary>
    public bool JudgeDelivery(Rigidbody rb)
    {
        bool correct = Matches(rb);
        if (patience == null) patience = GetComponent<CustomerPatience>();
        if (patience != null) patience.NotifyDelivered(correct);

        return correct;
    }

    /// <summary>尺寸和颜色是否都符合当前订单</summary>
    public bool Matches(Rigidbody rb)
    {
        if (rb == null) return false;

        GrabbableItem item = rb.GetComponentInParent<GrabbableItem>();
        ParcelFeature feature = rb.GetComponentInChildren<ParcelFeature>(true);
        if (item == null || feature == null) return false;

        return CurrentOrder.Size == ToOrderSize(item.Size)
            && CurrentOrder.SameColor(feature.CurrentColor);
    }

    public static OrderSize ToOrderSize(GrabbableItem.ItemSize size)
    {
        if (size == GrabbableItem.ItemSize.Medium) return OrderSize.Medium;
        if (size == GrabbableItem.ItemSize.Large) return OrderSize.Large;
        return OrderSize.Small;
    }

    // ---------- 订单表 ----------
    private void LoadOrders()
    {
        allOrders.Clear();

        // JSON 里是「若干纯色小/中快递混合后的结果」：
        // 两格 → 中快递，三格 → 大快递。同色结果会去重。
        LoadOrdersFromJson();

        if (allOrders.Count == 0) AddFallbackOrders();

        Debug.Log($"[CustomerOrder] 已生成 {allOrders.Count} 种订单", this);
    }

    private void AddFallbackOrders()
    {
        // JSON 没读到时的兜底，至少保证游戏还能玩
        AddOrder(OrderSize.Small, Color.red);
        AddOrder(OrderSize.Small, Color.green);
        AddOrder(OrderSize.Small, Color.blue);

        AddOrder(OrderSize.Medium, Color.red);
        AddOrder(OrderSize.Medium, Color.green);
        AddOrder(OrderSize.Medium, Color.blue);

        AddOrder(OrderSize.Large, Color.red);
        AddOrder(OrderSize.Large, Color.green);
        AddOrder(OrderSize.Large, Color.blue);
    }

    private void LoadOrdersFromJson()
    {
        HashSet<string> inputColors = new HashSet<string>();

        TextAsset asset = ColorMixJson;
        if (asset == null) asset = Resources.Load<TextAsset>(DefaultColorMixResource);

        if (asset == null)
        {
            Debug.LogWarning(
                $"[CustomerOrder] 找不到颜色混合表：请放到 Resources/{DefaultColorMixResource}.json 或在 Inspector 指定",
                this);
            return;
        }

        ColorMixTable table = JsonUtility.FromJson<ColorMixTable>(asset.text);
        if (table == null || table.mixes == null)
        {
            Debug.LogWarning("[CustomerOrder] 颜色混合表解析失败", this);
            return;
        }

        for (int i = 0; i < table.mixes.Length; i++)
        {
            ColorMixEntry entry = table.mixes[i];
            if (entry == null || entry.sizes == null || entry.result == null) continue;

            if (entry.colors != null)
            {
                for (int c = 0; c < entry.colors.Length; c++)
                {
                    if (!string.IsNullOrEmpty(entry.colors[c]))
                        inputColors.Add(entry.colors[c]);
                }
            }

            int units = 0;
            for (int s = 0; s < entry.sizes.Length; s++) units += entry.sizes[s];

            OrderSize size;
            if (units == 2) size = OrderSize.Medium;
            else if (units == 3) size = OrderSize.Large;
            else continue;                       // 当前只有 2 格 / 3 格配方

            Color color = new Color(
                entry.result.r / 255f,
                entry.result.g / 255f,
                entry.result.b / 255f,
                1f);

            AddOrder(size, color);
        }

        // 小快递只可能是未混合的纯色，颜色名同样来自 JSON
        foreach (string colorName in inputColors)
        {
            Color color;
            if (TryParseColorName(colorName, out color))
                AddOrder(OrderSize.Small, color);
        }
    }

    private static bool TryParseColorName(string name, out Color color)
    {
        switch (name.ToLowerInvariant())
        {
            case "red":
                color = Color.red;
                return true;
            case "green":
                color = Color.green;
                return true;
            case "blue":
                color = Color.blue;
                return true;
            default:
                color = Color.white;
                return false;
        }
    }

    private void AddOrder(OrderSize size, Color color)
    {
        Color32 c = color;

        for (int i = 0; i < allOrders.Count; i++)
        {
            if (allOrders[i].Size != size) continue;

            Color32 existing = allOrders[i].Color;
            if (existing.r == c.r && existing.g == c.g && existing.b == c.b)
                return;
        }

        allOrders.Add(new ParcelOrder(size, color));
    }

    // ---------- 显示 / 预览 ----------
    private void RefreshLabel()
    {
        if (!ShowLabel)
        {
            HideLabelIfNeeded();
            return;
        }

        if (Label == null) Label = GetComponentInChildren<TMP_Text>(true);
        if (Label == null)
        {
            Debug.LogWarning("[CustomerOrder] 没找到头顶的 TMP_Text", this);
            return;
        }

        Label.gameObject.SetActive(true);
        Label.text = CurrentOrder.DisplayText;
    }

    private void HideLabelIfNeeded()
    {
        if (!ShowLabel && Label != null) Label.gameObject.SetActive(false);
    }

    private void RefreshGhost()
    {
        if (!ghostVisible || Version <= 0)
        {
            if (ghost != null) ghost.Hide();
            return;
        }

        Transform table = ResolveGiveTable();
        if (table == null)
        {
            if (ghost != null) ghost.Hide();
            return;
        }

        if (ghost == null) ghost = new OrderGhostPreview();
        ghost.Show(table, CurrentOrder.Size, CurrentOrder.Color, GhostAlpha, GhostOutlineWidth);
    }

    private Transform ResolveGiveTable()
    {
        if (GiveTable != null) return GiveTable;

        // 优先拿场景里 GiveControl 实际使用的桌子，避免改名后失效
        GiveControl give = FindObjectOfType<GiveControl>();
        if (give != null && give.Table != null)
        {
            GiveTable = give.Table;
            return GiveTable;
        }

        GameObject go = GameObject.Find("Give");
        if (go != null) GiveTable = go.transform;

        return GiveTable;
    }
}
