using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ScrcpyStudio;

/// <summary>Attached properties shared by the control templates.</summary>
public static class Ui
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(Geometry), typeof(Ui), new PropertyMetadata(null));

    public static readonly DependencyProperty HintProperty = DependencyProperty.RegisterAttached(
        "Hint", typeof(string), typeof(Ui), new PropertyMetadata("", OnHintChanged));

    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached(
        "Radius", typeof(CornerRadius), typeof(Ui), new PropertyMetadata(new CornerRadius(8)));

    public static readonly DependencyProperty IsEmptyProperty = DependencyProperty.RegisterAttached(
        "IsEmpty", typeof(bool), typeof(Ui), new PropertyMetadata(true));

    public static Geometry? GetIcon(DependencyObject target) => (Geometry?)target.GetValue(IconProperty);
    public static void SetIcon(DependencyObject target, Geometry? value) => target.SetValue(IconProperty, value);

    public static string GetHint(DependencyObject target) => (string)target.GetValue(HintProperty);
    public static void SetHint(DependencyObject target, string value) => target.SetValue(HintProperty, value);

    public static CornerRadius GetRadius(DependencyObject target) => (CornerRadius)target.GetValue(RadiusProperty);
    public static void SetRadius(DependencyObject target, CornerRadius value) => target.SetValue(RadiusProperty, value);

    public static bool GetIsEmpty(DependencyObject target) => (bool)target.GetValue(IsEmptyProperty);
    public static void SetIsEmpty(DependencyObject target, bool value) => target.SetValue(IsEmptyProperty, value);

    private static void OnHintChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        // PasswordBox exposes no bindable text, so its placeholder follows PasswordChanged.
        if (target is not PasswordBox box)
            return;
        box.PasswordChanged -= OnPasswordChanged;
        box.PasswordChanged += OnPasswordChanged;
        SetIsEmpty(box, box.SecurePassword.Length == 0);
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        SetIsEmpty(box, box.SecurePassword.Length == 0);
    }

    internal static Geometry? Glyph(string name) => Application.Current.TryFindResource($"Icon.{name}") as Geometry;
}

/// <summary>Draws a 24 px grid stroke icon in the inherited foreground colour.</summary>
public sealed class Icon : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(Icon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(Icon), new FrameworkPropertyMetadata(Brushes.Black,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(16d,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(2d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FilledProperty = DependencyProperty.Register(
        nameof(Filled), typeof(bool), typeof(Icon),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double Stroke
    {
        get => (double)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public bool Filled
    {
        get => (bool)GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    protected override void OnRender(DrawingContext drawing)
    {
        if (Data is not { } data)
            return;

        var pen = new Pen(Foreground, Stroke)
        {
            StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round,
        };
        double scale = Size / 24;
        drawing.PushTransform(new ScaleTransform(scale, scale));
        drawing.DrawGeometry(Filled ? Foreground : null, pen, data);
        drawing.Pop();
    }
}
