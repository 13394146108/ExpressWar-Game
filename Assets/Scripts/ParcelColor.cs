using UnityEngine;

/// <summary>
/// 读出一件快递的颜色组合（位掩码），颜色定义和 Compose 保持一致：
///   黄（普通 / Normal）  = 1
///   蓝（易碎 / Fragile） = 2
///   红（易爆 / Explosive）= 4
///
/// 单色看 ParcelFeature.Type；Compose 合出来的混色大快递是 Type = Normal + 混色材质
/// （Y+R / Y+B / R+B / Y+R+B），所以 Type 为 Normal 时再看材质名里的大写字母。
/// </summary>
public static class ParcelColor
{
    public const int Yellow = 1;
    public const int Blue = 2;
    public const int Red = 4;

    /// <summary>读出这件快递的颜色组合；读不出来时按黄色处理</summary>
    public static int GetColorMask(Rigidbody rb)
    {
        if (rb == null) return 0;

        ParcelFeature feature = rb.GetComponentInChildren<ParcelFeature>(true);
        ParcelType type = feature != null ? feature.Type : ParcelType.Normal;

        if (type == ParcelType.Fragile) return Blue;
        if (type == ParcelType.Explosive) return Red;

        // 普通（黄）：可能是纯黄，也可能是合成出来的混色，看材质名
        foreach (Renderer r in rb.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r.sharedMaterial == null) continue;

            int mask = MaskFromMaterialName(r.sharedMaterial.name);
            if (mask != 0) return mask;
        }

        return Yellow;
    }

    /// <summary>材质名 → 颜色掩码，例如 "Y+R" → 黄|红；认不出来返回 0</summary>
    public static int MaskFromMaterialName(string materialName)
    {
        if (string.IsNullOrEmpty(materialName)) return 0;

        int mask = 0;
        if (materialName.IndexOf('Y') >= 0) mask |= Yellow;
        if (materialName.IndexOf('R') >= 0) mask |= Red;
        if (materialName.IndexOf('B') >= 0) mask |= Blue;

        return mask;
    }
}
