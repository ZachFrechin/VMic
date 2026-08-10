using System.Windows;
using System.Windows.Controls;

namespace Vmic.App.Controls;

/// <summary>
/// A horizontal peak meter. Bind <see cref="Level"/> to a 0..1 value; the fill
/// bar scales from the left edge.
/// </summary>
public partial class LevelMeterControl : UserControl
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(LevelMeterControl),
        new PropertyMetadata(0.0, OnLevelChanged));

    /// <summary>Level in the range 0..1 (values are clamped).</summary>
    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public LevelMeterControl()
    {
        InitializeComponent();
    }

    private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LevelMeterControl)d;
        double value = Math.Clamp((double)e.NewValue, 0.0, 1.0);
        control.FillScale.ScaleX = value;
    }
}
