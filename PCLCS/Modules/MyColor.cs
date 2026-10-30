// 由 VB Modules\Base\ModBase.vb 的 MyColor 类迁移而来。
// Widening Operator CType（双向隐式转换）在 C# 中以 implicit operator 实现，
// 覆盖 String / System.Windows.Media.Color / System.Drawing.Color / SolidColorBrush / Brush，
// VB 全项目 148 处引用经 Imports PCLCS.Modules 零改动解析。
#nullable disable

using System;
using System.Windows.Media;

namespace PCLCS.Modules;

/// <summary>
/// 支持小数与常见类型隐式转换的颜色。
/// </summary>
public class MyColor {

    public double A = 255;
    public double R = 0;
    public double G = 0;
    public double B = 0;

    // 类型转换
    public static implicit operator MyColor(string str) {
        return new MyColor(str);
    }
    public static implicit operator MyColor(Color col) {
        return new MyColor(col);
    }
    public static implicit operator Color(MyColor conv) {
        return Color.FromArgb(ClampToByte(conv.A), ClampToByte(conv.R), ClampToByte(conv.G), ClampToByte(conv.B));
    }
    public static implicit operator System.Drawing.Color(MyColor conv) {
        return System.Drawing.Color.FromArgb(ClampToByte(conv.A), ClampToByte(conv.R), ClampToByte(conv.G), ClampToByte(conv.B));
    }
    public static implicit operator MyColor(SolidColorBrush bru) {
        return new MyColor(bru.Color);
    }
    public static implicit operator SolidColorBrush(MyColor conv) {
        return new SolidColorBrush(Color.FromArgb(ClampToByte(conv.A), ClampToByte(conv.R), ClampToByte(conv.G), ClampToByte(conv.B)));
    }
    public static implicit operator MyColor(Brush bru) {
        return new MyColor(bru);
    }
    public static implicit operator Brush(MyColor conv) {
        return new SolidColorBrush(Color.FromArgb(ClampToByte(conv.A), ClampToByte(conv.R), ClampToByte(conv.G), ClampToByte(conv.B)));
    }

    // 颜色运算
    public static MyColor operator +(MyColor a, MyColor b) {
        return new MyColor { A = a.A + b.A, B = a.B + b.B, G = a.G + b.G, R = a.R + b.R };
    }
    public static MyColor operator -(MyColor a, MyColor b) {
        return new MyColor { A = a.A - b.A, B = a.B - b.B, G = a.G - b.G, R = a.R - b.R };
    }
    public static MyColor operator *(MyColor a, double b) {
        return new MyColor { A = a.A * b, B = a.B * b, G = a.G * b, R = a.R * b };
    }
    public static MyColor operator /(MyColor a, double b) {
        return new MyColor { A = a.A / b, B = a.B / b, G = a.G / b, R = a.R / b };
    }
    public static bool operator ==(MyColor a, MyColor b) {
        if (a is null && b is null) return true; // 对应 VB 的 IsNothing(a) AndAlso IsNothing(b)
        if (a is null || b is null) return false;
        return a.A == b.A && a.R == b.R && a.G == b.G && a.B == b.B;
    }
    public static bool operator !=(MyColor a, MyColor b) {
        if (a is null && b is null) return false;
        if (a is null || b is null) return true;
        return !(a.A == b.A && a.R == b.R && a.G == b.G && a.B == b.B);
    }
    /// <summary>
    /// 获取两颜色间的百分比，基于 RGB 计算。
    /// </summary>
    public static MyColor Lerp(MyColor ValueA, MyColor ValueB, double Percent) {
        return Round(ValueA * (1 - Percent) + ValueB * Percent, 6);
    }
    public static MyColor Round(MyColor col, int w = 0) {
        return new MyColor { A = Math.Round(col.A, w), R = Math.Round(col.R, w), G = Math.Round(col.G, w), B = Math.Round(col.B, w) };
    }
    /// <summary>
    /// 将一个数字限制为 0~255 的 Byte 值。
    /// </summary>
    public static byte ClampToByte(double d) {
        if (d < 0) d = 0;
        if (d > 255) d = 255;
        return (byte)Math.Round(d); // VB 隐式收窄（Double → Byte，银行家舍入）等价于 (byte)Math.Round(d)
    }

    // 构造函数
    public MyColor() {
    }
    public MyColor(Color col) {
        A = col.A;
        R = col.R;
        G = col.G;
        B = col.B;
    }
    public MyColor(string HexString) {
        Color StringColor = (Color)ColorConverter.ConvertFromString(HexString);
        A = StringColor.A;
        R = StringColor.R;
        G = StringColor.G;
        B = StringColor.B;
    }
    public MyColor(double newA, MyColor col) {
        A = newA;
        R = col.R;
        G = col.G;
        B = col.B;
    }
    public MyColor(double newR, double newG, double newB) {
        A = 255;
        R = newR;
        G = newG;
        B = newB;
    }
    public MyColor(double newA, double newR, double newG, double newB) {
        A = newA;
        R = newR;
        G = newG;
        B = newB;
    }
    public MyColor(Brush brush) {
        Color Color = ((SolidColorBrush)brush).Color;
        A = Color.A;
        R = Color.R;
        G = Color.G;
        B = Color.B;
    }
    public MyColor(SolidColorBrush brush) {
        Color Color = brush.Color;
        A = Color.A;
        R = Color.R;
        G = Color.G;
        B = Color.B;
    }
    public MyColor(object obj) {
        if (obj is null) {
            A = 255; R = 255; G = 255; B = 255;
        } else {
            if (obj is SolidColorBrush) {
                // 避免反复获取 Color 对象造成性能下降
                Color Color = ((SolidColorBrush)obj).Color;
                A = Color.A;
                R = Color.R;
                G = Color.G;
                B = Color.B;
            } else {
                // VB 中 obj.A / obj.R 为后期绑定访问，C# 使用 dynamic 保持行为一致
                A = ((dynamic)obj).A;
                R = ((dynamic)obj).R;
                G = ((dynamic)obj).G;
                B = ((dynamic)obj).B;
            }
        }
    }

    // HSL
    public double Hue(double v1, double v2, double vH) {
        if (vH < 0) vH += 1;
        if (vH > 1) vH -= 1;
        if (vH < 0.16667) return v1 + (v2 - v1) * 6 * vH;
        if (vH < 0.5) return v2;
        if (vH < 0.66667) return v1 + (v2 - v1) * (4 - vH * 6);
        return v1;
    }
    public MyColor FromHSL(double sH, double sS, double sL) {
        A = 255;
        if (sS == 0) {
            R = sL * 2.55; G = R; B = R;
            return this;
        }

        double H = sH / 360;
        double S = sS / 100;
        double L = sL / 100;
        S = (L < 0.5) ? (S * L + L) : (S * (1.0 - L) + L);
        L = 2 * L - S;
        R = 255 * Hue(L, S, H + 1 / 3d); // VB 的 / 为浮点除法，1 / 3 对应 C# 的 1 / 3d
        G = 255 * Hue(L, S, H);
        B = 255 * Hue(L, S, H - 1 / 3d);
        return this;
    }
    public MyColor FromHSL2(double sH, double sS, double sL) {
        A = 255;
        if (sS == 0) {
            R = sL * 2.55; G = R; B = R;
            return this;
        }

        // 初始化
        sH = (sH + 3600000) % 360;
        double[] cent = {
            +0.1, -0.06, -0.3, // 0, 30, 60
            -0.19, -0.15, -0.24, // 90, 120, 150
            -0.32, -0.09, +0.18, // 180, 210, 240
            +0.05, -0.12, -0.02, // 270, 300, 330
            +0.1, -0.06 }; // 最后两位与前两位一致，加是变亮，减是变暗
        // 计算色调对应的亮度片区
        double center = sH / 30.0;
        int intCenter = (int)Math.Floor(center); // 亮度片区编号
        center = 50 - (
             (1 - center + intCenter) * cent[intCenter] + (center - intCenter) * cent[intCenter + 1]
            ) * sS;
        sL = ((sL < center) ? (sL / center) : (1 + (sL - center) / (100 - center))) * 50;
        FromHSL(sH, sS, sL);
        return this;
    }

    public override string ToString() {
        return "(" + A + "," + R + "," + G + "," + B + ")";
    }
    public override bool Equals(object obj) {
        if (ReferenceEquals(null, obj)) return false; // VB IsNothing 语义：Me 非 Nothing 时与 Nothing 不相等
        return this == (MyColor)obj; // 等价于 VB 的 Return Me = obj，调用颜色比较运算符
    }
    // VB 中没有 GetHashCode。此处补充实现，使用 (A,R,G,B) 元组哈希，与 Equals 的四通道比较保持一致。
    public override int GetHashCode() {
        return (A, R, G, B).GetHashCode();
    }

}
