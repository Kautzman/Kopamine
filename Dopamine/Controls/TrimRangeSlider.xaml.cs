using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Dopamine.Controls
{
    /// <summary>
    /// A timeline with draggable start/end trim handles and a playhead.
    /// Values are in seconds. Clicking or dragging on the bar executes <see cref="SeekCommand"/>
    /// with the target position (a double), so the owner decides how to move the playhead.
    /// </summary>
    public partial class TrimRangeSlider : UserControl
    {
        private const double HandleWidth = 14.0;
        private const double TrackHeight = 10.0;

        private double dragGrabOffset;
        private bool isSeeking;

        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            nameof(Maximum), typeof(double), typeof(TrimRangeSlider), new PropertyMetadata(0.0, OnLayoutPropertyChanged));

        public static readonly DependencyProperty StartValueProperty = DependencyProperty.Register(
            nameof(StartValue), typeof(double), typeof(TrimRangeSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnLayoutPropertyChanged));

        public static readonly DependencyProperty EndValueProperty = DependencyProperty.Register(
            nameof(EndValue), typeof(double), typeof(TrimRangeSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnLayoutPropertyChanged));

        public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
            nameof(Position), typeof(double), typeof(TrimRangeSlider), new PropertyMetadata(0.0, OnLayoutPropertyChanged));

        public static readonly DependencyProperty MinimumRangeProperty = DependencyProperty.Register(
            nameof(MinimumRange), typeof(double), typeof(TrimRangeSlider), new PropertyMetadata(1.0));

        public static readonly DependencyProperty SeekCommandProperty = DependencyProperty.Register(
            nameof(SeekCommand), typeof(ICommand), typeof(TrimRangeSlider), new PropertyMetadata(null));

        public TrimRangeSlider()
        {
            InitializeComponent();
        }

        public double Maximum
        {
            get { return (double)GetValue(MaximumProperty); }
            set { SetValue(MaximumProperty, value); }
        }

        public double StartValue
        {
            get { return (double)GetValue(StartValueProperty); }
            set { SetValue(StartValueProperty, value); }
        }

        public double EndValue
        {
            get { return (double)GetValue(EndValueProperty); }
            set { SetValue(EndValueProperty, value); }
        }

        public double Position
        {
            get { return (double)GetValue(PositionProperty); }
            set { SetValue(PositionProperty, value); }
        }

        public double MinimumRange
        {
            get { return (double)GetValue(MinimumRangeProperty); }
            set { SetValue(MinimumRangeProperty, value); }
        }

        public ICommand SeekCommand
        {
            get { return (ICommand)GetValue(SeekCommandProperty); }
            set { SetValue(SeekCommandProperty, value); }
        }

        private double UsableWidth => Math.Max(1.0, this.RootCanvas.ActualWidth - HandleWidth);

        private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((TrimRangeSlider)d).UpdatePositions();
        }

        private double ValueToX(double value)
        {
            double ratio = this.Maximum > 0 ? Math.Max(0.0, Math.Min(1.0, value / this.Maximum)) : 0.0;
            return (HandleWidth / 2.0) + (ratio * this.UsableWidth);
        }

        private double XToValue(double x)
        {
            if (this.Maximum <= 0)
            {
                return 0;
            }

            double ratio = (x - (HandleWidth / 2.0)) / this.UsableWidth;
            return Math.Max(0.0, Math.Min(1.0, ratio)) * this.Maximum;
        }

        private void UpdatePositions()
        {
            double height = this.RootCanvas.ActualHeight;

            if (height <= 0)
            {
                return;
            }

            double trackTop = (height - TrackHeight) / 2.0;
            double startX = this.ValueToX(this.StartValue);
            double endX = this.ValueToX(this.EndValue);

            Canvas.SetLeft(this.TrackRectangle, HandleWidth / 2.0);
            Canvas.SetTop(this.TrackRectangle, trackTop);
            this.TrackRectangle.Width = this.UsableWidth;

            Canvas.SetLeft(this.SelectionRectangle, startX);
            Canvas.SetTop(this.SelectionRectangle, trackTop);
            this.SelectionRectangle.Width = Math.Max(0.0, endX - startX);

            Canvas.SetLeft(this.PlayheadRectangle, this.ValueToX(this.Position) - 1.0);
            Canvas.SetTop(this.PlayheadRectangle, 0);
            this.PlayheadRectangle.Height = height;

            PlaceThumb(this.StartThumb, startX, trackTop);
            PlaceThumb(this.EndThumb, endX, trackTop);
        }

        private static void PlaceThumb(Thumb thumb, double centerX, double trackTop)
        {
            // Same vertical band as the track: the handle never extends past the TrackRectangle
            thumb.Width = HandleWidth;
            thumb.Height = TrackHeight;
            Canvas.SetLeft(thumb, centerX - (HandleWidth / 2.0));
            Canvas.SetTop(thumb, trackTop);
        }

        private void RootCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            this.UpdatePositions();
        }

        private void Thumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            Thumb thumb = (Thumb)sender;
            double handleValue = thumb == this.StartThumb ? this.StartValue : this.EndValue;

            // Remember where inside the handle the user grabbed so it does not jump under the cursor
            this.dragGrabOffset = Mouse.GetPosition(this.RootCanvas).X - this.ValueToX(handleValue);
        }

        private void StartThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double value = this.XToValue(Mouse.GetPosition(this.RootCanvas).X - this.dragGrabOffset);
            double upperLimit = Math.Max(0.0, this.EndValue - this.MinimumRange);

            // SetCurrentValue keeps the TwoWay binding intact
            this.SetCurrentValue(StartValueProperty, Math.Max(0.0, Math.Min(upperLimit, value)));
        }

        private void EndThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double value = this.XToValue(Mouse.GetPosition(this.RootCanvas).X - this.dragGrabOffset);
            double lowerLimit = Math.Min(this.Maximum, this.StartValue + this.MinimumRange);

            this.SetCurrentValue(EndValueProperty, Math.Min(this.Maximum, Math.Max(lowerLimit, value)));
        }

        private void RootCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.isSeeking = this.RootCanvas.CaptureMouse();
            this.RequestSeek(e.GetPosition(this.RootCanvas).X);
            e.Handled = true;
        }

        private void RootCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (this.isSeeking && e.LeftButton == MouseButtonState.Pressed)
            {
                this.RequestSeek(e.GetPosition(this.RootCanvas).X);
            }
        }

        private void RootCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (this.isSeeking)
            {
                this.RootCanvas.ReleaseMouseCapture();
            }
        }

        private void RootCanvas_LostMouseCapture(object sender, MouseEventArgs e)
        {
            this.isSeeking = false;
        }

        private void RequestSeek(double x)
        {
            double value = this.XToValue(x);
            ICommand command = this.SeekCommand;

            if (command != null && command.CanExecute(value))
            {
                command.Execute(value);
            }
        }
    }
}
