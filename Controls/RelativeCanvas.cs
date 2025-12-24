using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Photo.Controls
{
    public class RelativeCanvas : Canvas
    {
        protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size availableSize)
        {
            foreach (UIElement child in Children)
            {
                child.Measure(availableSize);
            }
            return base.MeasureOverride(availableSize);
        }

        protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size finalSize)
        {
            foreach (UIElement child in Children)
            {
                if (child is FrameworkElement element)
                {
                    // Get relative coordinates attached properties
                    double relX = GetRelativeX(element);
                    double relY = GetRelativeY(element);
                    double relW = GetRelativeWidth(element);
                    double relH = GetRelativeHeight(element);

                    double x = relX * finalSize.Width;
                    double y = relY * finalSize.Height;
                    double w = relW * finalSize.Width;
                    double h = relH * finalSize.Height;

                    if (w > 0 && h > 0)
                    {
                        element.Width = w;
                        element.Height = h;
                    }

                    SetLeft(element, x);
                    SetTop(element, y);
                    
                    element.Arrange(new Windows.Foundation.Rect(x, y, w > 0 ? w : element.DesiredSize.Width, h > 0 ? h : element.DesiredSize.Height));
                }
                else
                {
                    child.Arrange(new Windows.Foundation.Rect(0, 0, finalSize.Width, finalSize.Height));
                }
            }
            return finalSize;
        }

        #region Attached Properties

        public static readonly DependencyProperty RelativeXProperty =
            DependencyProperty.RegisterAttached("RelativeX", typeof(double), typeof(RelativeCanvas), new PropertyMetadata(0.0, OnLayoutPropertyChanged));

        public static void SetRelativeX(DependencyObject element, double value) => element.SetValue(RelativeXProperty, value);
        public static double GetRelativeX(DependencyObject element) => (double)element.GetValue(RelativeXProperty);

        public static readonly DependencyProperty RelativeYProperty =
            DependencyProperty.RegisterAttached("RelativeY", typeof(double), typeof(RelativeCanvas), new PropertyMetadata(0.0, OnLayoutPropertyChanged));

        public static void SetRelativeY(DependencyObject element, double value) => element.SetValue(RelativeYProperty, value);
        public static double GetRelativeY(DependencyObject element) => (double)element.GetValue(RelativeYProperty);

        public static readonly DependencyProperty RelativeWidthProperty =
            DependencyProperty.RegisterAttached("RelativeWidth", typeof(double), typeof(RelativeCanvas), new PropertyMetadata(0.0, OnLayoutPropertyChanged));

        public static void SetRelativeWidth(DependencyObject element, double value) => element.SetValue(RelativeWidthProperty, value);
        public static double GetRelativeWidth(DependencyObject element) => (double)element.GetValue(RelativeWidthProperty);

        public static readonly DependencyProperty RelativeHeightProperty =
            DependencyProperty.RegisterAttached("RelativeHeight", typeof(double), typeof(RelativeCanvas), new PropertyMetadata(0.0, OnLayoutPropertyChanged));

        public static void SetRelativeHeight(DependencyObject element, double value) => element.SetValue(RelativeHeightProperty, value);
        public static double GetRelativeHeight(DependencyObject element) => (double)element.GetValue(RelativeHeightProperty);

        private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element && element.Parent is RelativeCanvas canvas)
            {
                canvas.InvalidateArrange();
            }
        }

        #endregion
    }
}
