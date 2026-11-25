using TMPro;
using UnityEngine;

/// <summary>顾客想要的快递尺寸</summary>
public enum OrderSize
{
    Small,
    Medium,
    Large
}

/// <summary>
/// 顾客订单：尺寸 + 颜色组合。
/// 颜色用位掩码，和 Compose 里的定义保持一致：
///   黄（普通 / Normal） = 1
///   蓝（易碎 / Fragile）= 2
///   红（易爆 / Explosive）= 4
/// 显示顺序固定为 Yellow → Red → Blue，例如 Large Red+Blue、Large Yellow+Red+Blue。
/// </summary>
[System.Serializable]
public struct ParcelOrder
{
    public const int Yellow = 1;
    public const int Blue = 2;
    public const int Red = 4;

    public OrderSize Size;
    public int ColorMask;

    public ParcelOrder(OrderSize size, int colorMask)
    {
        Size = size;
        ColorMask = colorMask;
    }

    /// <summary>显示成 Small Red / Large Red+Blue 这样的文字</summary>
    public string DisplayText
    {
        get
        {
            string sizeText;
            if (Size == OrderSize.Small) sizeText = "Small";
            else if (Size == OrderSize.Medium) sizeText = "Medium";
            else sizeText = "Large";

            string colorText = "";
            Append(ref colorText, Yellow, "Yellow");
            Append(ref colorText, Red, "Red");
            Append(ref colorText, Blue, "Blue");

            return colorText.Length == 0 ? sizeText : sizeText + " " + colorText;
        }
    }

    private void Append(ref string text, int bit, string colorName)
    {
        if ((ColorMask & bit) == 0) return;

        text = text.Length == 0 ? colorName : text + "+" + colorName;
    }
}

/// <summary>
/// 顾客需求：开局在 13 种组合里随机一个，写到顾客头顶的 Text 上。
/// 以后要让文字变，直接调用 RollNewOrder() 或 SetOrder(...)。
/// 挂在 customer 上，Label 留空会自动在子物体里找 TMP_Text。
/// </summary>
[DisallowMultipleComponent]
public class CustomerOrder : MonoBehaviour
{
    [Header("显示")]
    [Tooltip("顾客头顶的文字；留空时自动在子物体里找 TMP_Text")]
    public TMP_Text Label;

    [Tooltip("开局随机一个订单")]
    public bool RandomizeOnStart = true;

    [Header("运行时（只读）")]
    [Tooltip("当前订单")]
    [System.NonSerialized] public ParcelOrder CurrentOrder;

    /// <summary>全部 13 种组合：小 3 + 中 3 + 大 7</summary>
    public static readonly ParcelOrder[] AllOrders =
    {
        // 小快递：红 / 蓝 / 黄
        new ParcelOrder(OrderSize.Small, ParcelOrder.Yellow),
        new ParcelOrder(OrderSize.Small, ParcelOrder.Blue),
        new ParcelOrder(OrderSize.Small, ParcelOrder.Red),

        // 中快递：红 / 蓝 / 黄
        new ParcelOrder(OrderSize.Medium, ParcelOrder.Yellow),
        new ParcelOrder(OrderSize.Medium, ParcelOrder.Blue),
        new ParcelOrder(OrderSize.Medium, ParcelOrder.Red),

        // 大快递：单色 3 种
        new ParcelOrder(OrderSize.Large, ParcelOrder.Yellow),
        new ParcelOrder(OrderSize.Large, ParcelOrder.Blue),
        new ParcelOrder(OrderSize.Large, ParcelOrder.Red),

        // 大快递：双色 3 种
        new ParcelOrder(OrderSize.Large, ParcelOrder.Yellow | ParcelOrder.Blue),
        new ParcelOrder(OrderSize.Large, ParcelOrder.Yellow | ParcelOrder.Red),
        new ParcelOrder(OrderSize.Large, ParcelOrder.Red | ParcelOrder.Blue),

        // 大快递：三色 1 种
        new ParcelOrder(OrderSize.Large,
                        ParcelOrder.Yellow | ParcelOrder.Red | ParcelOrder.Blue),
    };

    void Awake()
    {
        if (Label == null) Label = GetComponentInChildren<TMP_Text>(true);
    }

    void Start()
    {
        if (RandomizeOnStart) RollNewOrder();
    }

    /// <summary>订单变化（生成新需求时触发），耐心条用它来重置</summary>
    public event System.Action<ParcelOrder> OrderChanged;

    /// <summary>每次换需求自增，用来判断需求是否已经被别人换过</summary>
    public int Version { get; private set; }

    /// <summary>随机一个新订单并刷新头顶文字</summary>
    public void RollNewOrder()
    {
        SetOrder(AllOrders[Random.Range(0, AllOrders.Length)]);
    }

    /// <summary>指定订单并刷新头顶文字</summary>
    public void SetOrder(ParcelOrder order)
    {
        CurrentOrder = order;
        Version++;
        RefreshLabel();
        OrderChanged?.Invoke(order);
    }

    private void RefreshLabel()
    {
        if (Label == null) Label = GetComponentInChildren<TMP_Text>(true);
        if (Label == null)
        {
            Debug.LogWarning("[CustomerOrder] 没找到头顶的 TMP_Text", this);
            return;
        }

        Label.text = CurrentOrder.DisplayText;
    }

    /// <summary>以后做交付判定用：这件快递是不是顾客要的</summary>
    public bool Wants(OrderSize size, int colorMask) =>
        CurrentOrder.Size == size && CurrentOrder.ColorMask == colorMask;
}
