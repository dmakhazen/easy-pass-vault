using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Util;
using Android.Views;

namespace EasyPassVault.Droid;

/// <summary>Colours of the app icon and small builders for views made in code (there are no layout files).</summary>
internal static class Ui
{
    public static readonly Color Green = Color.ParseColor("#226549");
    public static readonly Color Cream = Color.ParseColor("#F8F4E9");
    public static readonly Color Paper = Color.ParseColor("#FBF9F3");
    public static readonly Color Line = Color.ParseColor("#DDD6C4");
    public static readonly Color Ink = Color.ParseColor("#1E2A25");
    public static readonly Color Muted = Color.ParseColor("#6B7570");
    public static readonly Color Error = Color.ParseColor("#B3261E");

    public static int Dp(this Context context, float value) =>
        (int)TypedValue.ApplyDimension(ComplexUnitType.Dip, value, context.Resources!.DisplayMetrics);

    public static Drawable Box(Context context, Color fill, float radiusDp, Color? stroke = null)
    {
        var box = new GradientDrawable();
        box.SetColor(fill);
        box.SetCornerRadius(context.Dp(radiusDp));
        if (stroke is { } color) box.SetStroke(context.Dp(1), color);
        return box;
    }

    public static TextView Label(Context context, string text, float sp, Color color, bool bold = false)
    {
        var view = new TextView(context) { Text = text };
        view.SetTextSize(ComplexUnitType.Sp, sp);
        view.SetTextColor(color);
        if (bold) view.SetTypeface(Typeface.Default, TypefaceStyle.Bold);
        return view;
    }

    public static Button MakeButton(Context context, string text, bool primary, Action click, bool small = false)
    {
        var button = new Button(context) { Text = text };
        button.SetAllCaps(false);
        button.SetTextSize(ComplexUnitType.Sp, small ? 14 : 16);
        button.SetTextColor(primary ? Color.White : Green);
        button.StateListAnimator = null;
        button.SetMinHeight(0);
        button.SetMinimumHeight(0);
        button.SetMinWidth(0);
        button.SetMinimumWidth(0);
        int horizontal = context.Dp(small ? 14 : 18), vertical = context.Dp(small ? 9 : 14);
        button.SetPadding(horizontal, vertical, horizontal, vertical);
        var shape = Box(context, primary ? Green : Color.White, small ? 10 : 12, primary ? null : Line);
        button.Background = new RippleDrawable(ColorStateList.ValueOf(Color.Argb(60, 0, 0, 0)), shape, shape);
        button.Click += (_, _) => click();
        return button;
    }

    public static EditText Input(Context context, string hint, bool password)
    {
        var input = new EditText(context) { Hint = hint };
        input.SetTextSize(ComplexUnitType.Sp, 16);
        input.SetTextColor(Ink);
        input.SetHintTextColor(Muted);
        input.SetSingleLine(true);
        input.InputType = password
            ? InputTypes.ClassText | InputTypes.TextVariationPassword
            : InputTypes.ClassText | InputTypes.TextFlagNoSuggestions;
        input.Background = Box(context, Color.White, 12, Line);
        input.TextAlignment = TextAlignment.ViewStart;
        int horizontal = context.Dp(14), vertical = context.Dp(13);
        input.SetPadding(horizontal, vertical, horizontal, vertical);
        return input;
    }

    /// <summary>Layout parameters for a child of a vertical column: full width, own height, a gap above.</summary>
    public static LinearLayout.LayoutParams Below(Context context, float gapDp, int height = ViewGroup.LayoutParams.WrapContent, float weight = 0) =>
        new(ViewGroup.LayoutParams.MatchParent, height, weight) { TopMargin = context.Dp(gapDp) };

    /// <summary>Layout parameters for a child of a row: a gap on the left; weight 1 takes the free width.</summary>
    public static LinearLayout.LayoutParams Beside(Context context, float gapDp, float weight = 0) =>
        new(weight > 0 ? 0 : ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, weight) { MarginStart = context.Dp(gapDp) };
}
