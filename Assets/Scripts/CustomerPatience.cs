using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 顾客耐心条：
///   1. 顾客提出需求后一直自然下降，满格到空约 DrainSeconds 秒（默认 40 秒）；
///   2. 交付错误直接扣一整格（MaxSteps 分之一）；
///   3. 每次生成新需求时自动回满并重新计时。
/// 挂在 customer 上，Fill 指向 Canvas 下的 Fill 图片（Image，Type = Filled）。
/// </summary>
[DisallowMultipleComponent]
public class CustomerPatience : MonoBehaviour
{
    [Header("滑块")]
    [Tooltip("Fill 图片；留空时自动在子物体里找名为 Fill 的 Image")]
    public Image Fill;

    [Tooltip("一共几节")]
    [Min(1)] public int MaxSteps = 8;

    [Header("消耗")]
    [Tooltip("从满格自然消耗到空需要的秒数")]
    [Min(0.1f)] public float DrainSeconds = 40f;

    [Tooltip("交付正确时是否回升一格（默认只降不升）")]
    public bool RecoverOnCorrect = false;

    [Header("运行时（只读）")]
    [Tooltip("当前耐心值（0 ~ MaxSteps，浮点，随时间连续下降）")]
    [System.NonSerialized] public float Patience;

    /// <summary>格数变化：参数为「当前格数, 总格数」</summary>
    public event System.Action<int, int> StepsChanged;

    /// <summary>耐心耗尽</summary>
    public event System.Action Depleted;

    private CustomerOrder order;
    private int lastStep;
    private bool depleted;

    /// <summary>当前还剩几格（向上取整）</summary>
    public int CurrentSteps => Mathf.CeilToInt(Mathf.Max(0f, Patience));

    /// <summary>0~1 的进度</summary>
    public float Normalized => MaxSteps > 0 ? Mathf.Clamp01(Patience / MaxSteps) : 0f;

    public bool IsEmpty => Patience <= 0f;

    void Awake()
    {
        if (Fill == null) Fill = FindFillImage();
        order = GetComponent<CustomerOrder>();
        ResetSteps();
    }

    void OnEnable()
    {
        if (order == null) order = GetComponent<CustomerOrder>();
        if (order != null) order.OrderChanged += OnOrderChanged;
    }

    void OnDisable()
    {
        if (order != null) order.OrderChanged -= OnOrderChanged;
    }

    /// <summary>顾客换需求 → 耐心条回满，重新开始倒计时</summary>
    private void OnOrderChanged(ParcelOrder newOrder)
    {
        ResetSteps();
    }

    void Update()
    {
        if (Patience <= 0f) return;

        // 满格 ÷ 秒数 = 每秒掉多少格
        float perSecond = MaxSteps / Mathf.Max(0.1f, DrainSeconds);
        Patience -= perSecond * Time.deltaTime;
        if (Patience < 0f) Patience = 0f;

        ApplyFill();

        if (Patience <= 0f) OnDepleted();
    }

    /// <summary>由 GiveControl 在交付判定后调用：放错直接扣一格</summary>
    public void NotifyDelivered(bool correct)
    {
        if (correct)
        {
            if (!RecoverOnCorrect) return;
            Patience = Mathf.Min(MaxSteps, Patience + 1f);
        }
        else
        {
            Patience = Mathf.Max(0f, Patience - 1f);   // 直接少一整格
        }

        ApplyFill();

        if (Patience <= 0f) OnDepleted();
    }

    /// <summary>回满并重新计时</summary>
    public void ResetSteps()
    {
        Patience = MaxSteps;
        depleted = false;
        lastStep = MaxSteps;

        ApplyFill();
        StepsChanged?.Invoke(MaxSteps, MaxSteps);
    }

    private void ApplyFill()
    {
        if (Fill != null)
            Fill.fillAmount = MaxSteps > 0 ? Mathf.Clamp01(Patience / MaxSteps) : 0f;

        int step = CurrentSteps;
        if (step != lastStep)
        {
            lastStep = step;
            StepsChanged?.Invoke(step, MaxSteps);
        }
    }

    private void OnDepleted()
    {
        if (depleted) return;

        depleted = true;
        Debug.Log("[CustomerPatience] 耐心耗尽", this);
        Depleted?.Invoke();

        // 耐心见底 → 顾客换一个新需求；SetOrder 会触发 OrderChanged 把耐心条重新填满
        if (order != null) order.RollNewOrder();
    }

    private Image FindFillImage()
    {
        foreach (Image img in GetComponentsInChildren<Image>(true))
        {
            if (img.gameObject.name == "Fill") return img;
        }

        foreach (Image img in GetComponentsInChildren<Image>(true))
        {
            if (img.type == Image.Type.Filled) return img;
        }

        return null;
    }
}
