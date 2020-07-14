using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using CompositionProToolkit;
using CompositionProToolkit.Win2d;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace LiquidSwipe
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPageWithSwipe : Page
    {
        private enum SlideDirection
        {
            None,
            LeftToRight,
            RightToLeft,
        }

        private enum SwipeDirection
        {
            None,
            Left,
            Right
        }

        public const int MaxShapeCount = 5;

        private Compositor _c;
        private ICompositionGenerator _g;
        private ShapeVisual _rootShape;

        private Stopwatch _sw0;
        private Stopwatch _sw1;
        private Stopwatch _sw2;
        private TimeSpan _a0;
        private TimeSpan _a1;
        private const float SwipeLeftStart = 0f;
        private const float SwipeLeftEnd = 1.1f;
        private const float SwipeRightStart = 1f;
        private const float SwipeRightEnd = -0.5f;

        private float _startVal;
        private float _endVal;

        private float _revealPercent;
        private float _verticalReveal;
        private float waveCenterY;
        private float waveHorRadius;
        private float waveVertRadius;
        private float sideWidth;
        private SlideDirection slideDirection;
        private readonly Color[] _colors;
        private int _selIndex;

        private readonly Vector2 HideOffset = new Vector2(100, 0);
        private const float MinOffsetX = 0f;
        private const float MaxOffsetX = 100f;
        private float _startOffsetX;
        private float _endOffsetX;
        private float _currOffsetX;
        private const float DefaultOffsetAnimDuration = 350;

        private Size _rootSize;

        // Interaction
        private Point _swipeStartPoint;
        private bool _isPointerDown;
        private bool _isSwiping;
        private SwipeDirection _swipeDir;
        private int _swipeLeftIndex;
        private int _swipeRightIndex;
        private int _nextLeftIndex;
        private int _nextRightIndex;
        private int _nextShapeIndex;
        private const float SwipeDistanceThreshold = 20f;
        private const float DefaultGeometryAnimDuration = 500;
        private const float SwipeThreshold = 0.25f;
        private const float VerticalRevealFactor = 1.5f;
        private readonly TimeSpan SwipeThresholdDuration = TimeSpan.FromMilliseconds(450);

        public MainPageWithSwipe()
        {
            InitializeComponent();

            _colors = new[]
            {
                Colors.Crimson,
                CanvasObject.CreateColor("#007aff"),
                CanvasObject.CreateColor("#4cd964"),
                CanvasObject.CreateColor("#ff2d55"),
                CanvasObject.CreateColor("#ff9600")
            };

            // Keeps track of the duration of the swipe
            _sw0 = new Stopwatch();
            _sw0.Reset();
            // Timeline for the top layer whose geometry is being animated
            _sw1 = new Stopwatch();
            _sw1.Reset();
            // Timeline for the lower layer whose offset is being animated
            _sw2 = new Stopwatch();
            _sw2.Reset();

            // The bump will appear at 2/3rd of the height from the top
            _verticalReveal = 1f;
            _swipeDir = SwipeDirection.None;

            // Duration of the top layer geometry animation
            _a0 = TimeSpan.FromMilliseconds(DefaultGeometryAnimDuration);
            // Duration of the lower layer offset animation
            _a1 = TimeSpan.FromMilliseconds(DefaultOffsetAnimDuration);
        }

        private void OnCreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args)
        {
            _c = Window.Current.Compositor;
            _g = _c.CreateCompositionGenerator();

            _rootShape = _c.CreateShapeVisual();
            _rootShape.Size = new Vector2((float)RenderGrid.ActualWidth, (float)RenderGrid.ActualHeight);
            _rootSize = _rootShape.Size.ToSize();

            var rectGeom = _c.CreatePathGeometry(new CompositionPath(CanvasGeometry.CreateRectangle(_g.Device, new Rect(new Point(), _rootSize))));
            var bgShape = _c.CreateSpriteShape(rectGeom);
            bgShape.FillBrush = _c.CreateColorBrush(CanvasObject.CreateColor("#161616"));

            _rootShape.Shapes.Add(bgShape);

            //_slideLeftImplicitAnimationCollection = _c.CreateImplicitAnimationCollection();
            //var slideLeftAnim = _c.GenerateVector2KeyFrameAnimation()
            //                   .HavingDuration(700)
            //                   .ForTarget(() => _c.CreateSpriteShape().Offset);
            //slideLeftAnim.InsertFinalValueKeyFrame(1f, _c.CreateEaseInOutBackEasingFunction());
            //_slideLeftImplicitAnimationCollection["Offset"] = slideLeftAnim.Animation;

            //_slideRightImplicitAnimationCollection = _c.CreateImplicitAnimationCollection();
            //var slideRightAnim = _c.GenerateVector2KeyFrameAnimation()
            //    .HavingDuration(2000)
            //    .ForTarget(() => _c.CreateSpriteShape().Offset);
            //slideRightAnim.InsertFinalValueKeyFrame(1f, _c.CreateEaseInOutBackEasingFunction());
            //_slideRightImplicitAnimationCollection["Offset"] = slideRightAnim.Animation;

            for (var i = 0; i < MaxShapeCount; i++)
            {
                var shape = _c.CreateSpriteShape(_c.CreatePathGeometry(GetClip(_rootSize, 0f)));
                // Offset each of the shape to the right to hide the bump of lower layers
                shape.Offset = HideOffset;
                shape.FillBrush = _c.CreateColorBrush(_colors[i]);
                _rootShape.Shapes.Add(shape);
            }

            _selIndex = -1;
            _swipeRightIndex = -1;
            _swipeLeftIndex = MaxShapeCount;
            _nextShapeIndex = -1;
            // Reset offset of top most shape
            _rootShape.Shapes[MaxShapeCount].Offset = Vector2.Zero;

            ElementCompositionPreview.SetElementChildVisual(RenderGrid, _rootShape);
            //PrevBtn.IsEnabled = false;
        }

        private void OnDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
        {
            if ((_rootShape == null))
            {
                return;
            }

            try
            {
                if (_isSwiping && _selIndex != -1)
                {
                    Debug.WriteLine($"Rendering Geometry for _selIndex = {_selIndex} at _revealPercent = {_revealPercent}");
                    ((CompositionPathGeometry)((CompositionSpriteShape)_rootShape.Shapes.ElementAt(_selIndex)).Geometry).Path = GetClip(_rootSize, _revealPercent);

                    if (_nextShapeIndex != -1)
                    {
                        Debug.WriteLine($"Moving _nextShapeIndex = {_nextShapeIndex} to ({_currOffsetX}, 0)");
                        ((CompositionPathGeometry)((CompositionSpriteShape)_rootShape.Shapes.ElementAt(_nextShapeIndex)).Geometry).Path = GetClip(_rootSize, 0f);
                        ((CompositionSpriteShape)_rootShape.Shapes.ElementAt(_nextShapeIndex)).Offset = new Vector2(_currOffsetX, 0);
                    }

                    return;
                }

                if (_sw1.IsRunning)
                {
                    var elapsed = _sw1.ElapsedMilliseconds;
                    var t = Math.Min(1f, (float)(elapsed / _a0.TotalMilliseconds));
                    var revealPercent = Lerp(_startVal, _endVal, EaseInOut(t));

                    ((CompositionPathGeometry)((CompositionSpriteShape)_rootShape.Shapes.ElementAt(_selIndex)).Geometry).Path = GetClip(_rootSize, revealPercent);

                    Debug.WriteLine($"\tRendering Geometry anim for _selIndex = {_selIndex} at revealPercent = {revealPercent} _startVal = {_startVal} _endVal = {_endVal}");
                    if (elapsed >= (long)_a0.TotalMilliseconds)
                    {
                        _sw1.Reset();
                        _selIndex = -1;
                        _swipeLeftIndex = _nextLeftIndex;
                        _swipeRightIndex = _nextRightIndex;

                        Debug.WriteLine($"\t\t_swipeRightIndex = {_swipeRightIndex} _swipeLeftIndex = {_swipeLeftIndex}");
                    }
                }

                if (_sw2.IsRunning && _nextShapeIndex != -1)
                {
                    var elapsed = _sw2.ElapsedMilliseconds;
                    var t1 = Math.Min(1f, (float)(elapsed / _a1.TotalMilliseconds));
                    var offsetX = Lerp(_startOffsetX, _endOffsetX, EaseOut(t1));
                    ((CompositionSpriteShape)_rootShape.Shapes.ElementAt(_nextShapeIndex)).Offset = new Vector2(offsetX, 0);
                    Debug.WriteLine($"\tRendering offset anim for nextShapeIndex = {_nextShapeIndex} at ({offsetX}, 0)");

                    if (elapsed >= (long)_a1.TotalMilliseconds)
                    {
                        _sw2.Reset();
                        _nextShapeIndex = -1;
                    }
                }
            }
            catch (Exception e)
            {
                var msg = e.Message;
            }
        }

        #region Helper Methods

        ///
        /// Animation Helper methods based on https://www.febucci.com/2018/08/easing-functions/
        /// 
        public static float EaseIn(float t)
        {
            return t * t;
        }

        public static float EaseOut(float t)
        {
            t = 1f - t;
            return (1f - (t * t));
        }

        public static float EaseInOut(float t)
        {
            return Lerp(EaseIn(t), EaseOut(t), t);
        }

        private static float Lerp(float start_value, float end_value, float pct)
        {
            return (start_value + (end_value - start_value) * pct);
        }

        ///
        /// The following helper methods are based on
        /// https://github.com/iamSahdeep/liquid_swipe_flutter/blob/master/lib/Clippers/WaveLayer.dart 
        ///
        private CompositionPath GetClip(Size size, float revealPercent)
        {
            CanvasPathBuilder pB = new CanvasPathBuilder(_g.Device);
            sideWidth = GetSideWidth(size, revealPercent);
            waveVertRadius = GetWaveVerticalRadiusF(size, revealPercent);

            waveCenterY = (float)(size.Height * (2 * _verticalReveal / 3));
            waveHorRadius = slideDirection == SlideDirection.RightToLeft
                ? GetWaveHorizontalRadiusFBack(size, revealPercent)
                : GetWaveHorizontalRadiusF(size, revealPercent);

            var maskWidth = (float)(size.Width - sideWidth);
            pB.BeginFigure(new Vector2(maskWidth - sideWidth, 0));
            pB.AddLine(new Vector2(0, 0));
            pB.AddLine(new Vector2(0, (float)(size.Height)));
            pB.AddLine(new Vector2(maskWidth, (float)(size.Height)));
            float curveStartY = waveCenterY + waveVertRadius;

            pB.AddLine(new Vector2(maskWidth, curveStartY));

            pB.AddCubicBezier(
              new Vector2((float)(maskWidth),
                          (float)(curveStartY - waveVertRadius * 0.1346194756)),
              new Vector2((float)(maskWidth - waveHorRadius * 0.05341339583),
                          (float)(curveStartY - waveVertRadius * 0.2412779634)),
              new Vector2((float)(maskWidth - waveHorRadius * 0.1561501458),
                          (float)(curveStartY - waveVertRadius * 0.3322374268))
            );

            pB.AddCubicBezier(
              new Vector2((float)(maskWidth - waveHorRadius * 0.2361659167),
                          (float)(curveStartY - waveVertRadius * 0.4030805244)),
              new Vector2((float)(maskWidth - waveHorRadius * 0.3305285625),
                          (float)(curveStartY - waveVertRadius * 0.4561193293)),
              new Vector2((float)(maskWidth - waveHorRadius * 0.5012484792),
                          (float)(curveStartY - waveVertRadius * 0.5350576951))
            );

            pB.AddCubicBezier(
              new Vector2((float)(maskWidth - waveHorRadius * 0.515878125),
                          (float)(curveStartY - waveVertRadius * 0.5418222317)),
              new Vector2((float)(maskWidth - waveHorRadius * 0.5664134792),
                          (float)(curveStartY - waveVertRadius * 0.5650349878)),
              new Vector2((float)(maskWidth - waveHorRadius * 0.574934875),
                          (float)(curveStartY - waveVertRadius * 0.5689655122))
            );

            pB.AddCubicBezier(
              new Vector2((float)(maskWidth - waveHorRadius * 0.7283715208),
                          (float)(curveStartY - waveVertRadius * 0.6397387195)),
              new Vector2((float)(maskWidth - waveHorRadius * 0.8086618958),
                          (float)(curveStartY - waveVertRadius * 0.6833456585)),
              new Vector2((float)(maskWidth - waveHorRadius * 0.8774032292),
                          (float)(curveStartY - waveVertRadius * 0.7399037439))
            );

            pB.AddCubicBezier(
              new Vector2((float)(maskWidth - waveHorRadius * 0.9653464583),
                          (float)(curveStartY - waveVertRadius * 0.8122605122)),
              new Vector2((float)(maskWidth - waveHorRadius),
                          (float)(curveStartY - waveVertRadius * 0.8936183659)),
              new Vector2((float)(maskWidth - waveHorRadius),
                          (float)(curveStartY - waveVertRadius))
            );

            pB.AddCubicBezier(
              new Vector2((float)(maskWidth - waveHorRadius),
                          (float)(curveStartY - waveVertRadius * 1.100142878)),
              new Vector2((float)(maskWidth - waveHorRadius * 0.9595746667),
                          (float)(curveStartY - waveVertRadius * 1.1887991951)),
              new Vector2((float)(maskWidth - waveHorRadius * 0.8608411667),
                          (float)(curveStartY - waveVertRadius * 1.270484439))
            );

            pB.AddCubicBezier(
              new Vector2((float)(maskWidth - waveHorRadius * 0.7852123333),
                          (float)(curveStartY - waveVertRadius * 1.3330544756)),
              new Vector2((float)(maskWidth - waveHorRadius * 0.703382125),
                          (float)(curveStartY - waveVertRadius * 1.3795848049)),
              new Vector2((float)(maskWidth - waveHorRadius * 0.5291125625),
                          (float)(curveStartY - waveVertRadius * 1.4665102805))
            );

            pB.AddCubicBezier(
              new Vector2((float)(maskWidth - waveHorRadius * 0.5241858333),
                          (float)(curveStartY - waveVertRadius * 1.4689677195)),
              new Vector2((float)(maskWidth - waveHorRadius * 0.505739125),
                          (float)(curveStartY - waveVertRadius * 1.4781625854)),
              new Vector2((float)(maskWidth - waveHorRadius * 0.5015305417),
                          (float)(curveStartY - waveVertRadius * 1.4802616098))
            );

            pB.AddCubicBezier(
              new Vector2((float)(maskWidth - waveHorRadius * 0.3187486042),
                          (float)(curveStartY - waveVertRadius * 1.5714239024)),
              new Vector2((float)(maskWidth - waveHorRadius * 0.2332057083),
                          (float)(curveStartY - waveVertRadius * 1.6204116463)),
              new Vector2((float)(maskWidth - waveHorRadius * 0.1541165417),
                          (float)(curveStartY - waveVertRadius * 1.687403))
            );

            pB.AddCubicBezier(
              new Vector2((float)(maskWidth - waveHorRadius * 0.0509933125),
                          (float)(curveStartY - waveVertRadius * 1.774752061)),
              new Vector2((float)(maskWidth),
                          (float)(curveStartY - waveVertRadius * 1.8709256829)),
              new Vector2((float)(maskWidth),
                          (float)(curveStartY - waveVertRadius * 2))
            );

            pB.AddLine(new Vector2(maskWidth, 0));
            pB.EndFigure(CanvasFigureLoop.Closed);

            return new CompositionPath(CanvasGeometry.CreatePath(pB));
        }

        private float GetSideWidth(Size size, float revealPercent)
        {
            var p1 = 0.2;
            var p2 = 0.8;

            if (revealPercent <= p1)
            {
                return 0f;
            }

            if (revealPercent >= p2)
            {
                return (float)size.Width;
            }

            return (float)(15.0 + (size.Width - 15.0) * (revealPercent - p1) / (p2 - p1));
        }

        private float GetWaveVerticalRadiusF(Size size, float revealPercent)
        {
            var p1 = 0.4f;

            if (revealPercent <= 0f)
            {
                return 82.0f;
            }

            if (revealPercent >= p1)
            {
                return (float)(size.Height * 0.9);
            }

            return (float)(82.0 + ((size.Height * 0.9) - 82.0) * revealPercent / p1);
        }

        private float GetWaveHorizontalRadiusF(Size size, float revealPercent)
        {
            if (revealPercent <= 0)
            {
                return 48;
            }

            if (revealPercent >= 1)
            {
                return 0;
            }

            var p1 = 0.4;
            if (revealPercent <= p1)
            {
                return (float)(48.0 + revealPercent / p1 * ((size.Width * 0.8) - 48.0));
            }

            var t = (revealPercent - p1) / (1.0 - p1);
            var A = size.Width * 0.8;
            var r = 40;
            var m = 9.8;
            var beta = r / (2 * m);
            var k = 50;
            var omega0 = k / m;
            var omega = Math.Pow(-Math.Pow(beta, 2) + Math.Pow(omega0, 2), 0.5);

            return (float)(A * Math.Exp(-beta * t) * Math.Cos(omega * t));
        }

        private float GetWaveHorizontalRadiusFBack(Size size, float revealPercent)
        {
            if (revealPercent <= 0)
            {
                return 48;
            }

            if (revealPercent >= 1)
            {
                return 0;
            }

            var p1 = 0.4;
            if (revealPercent <= p1)
            {
                return (float)(48.0 + revealPercent / p1 * 48.0);
            }

            var t = (revealPercent - p1) / (1.0 - p1);
            var A = 96;
            var r = 40;
            var m = 9.8;
            var beta = r / (2 * m);
            var k = 50;
            var omega0 = k / m;
            var omega = (float)(Math.Pow(-Math.Pow(beta, 2) + Math.Pow(omega0, 2), 0.5));

            return (float)(A * Math.Exp(-beta * t) * Math.Cos(omega * t));
        }

        #endregion

        private void HandlePointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_sw1.IsRunning || _sw1.IsRunning)
            {
                return;
            }

            RenderGrid.CapturePointer(e.Pointer);
            _swipeStartPoint = e.GetCurrentPoint(RenderGrid).Position;
            // Position the bubble at the same height as the touch
            _verticalReveal = (float)(_swipeStartPoint.Y / _rootSize.Height) * VerticalRevealFactor;
            _sw0.Restart();
            _swipeDir = SwipeDirection.None;
            _isPointerDown = true;
        }

        private void HandlePointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPointerDown)
            {
                return;
            }

            var currPoint = e.GetCurrentPoint(RenderGrid).Position;
            if (currPoint.X.IsCloseTo(_swipeStartPoint.X))
            {
                return;
            }

            // Position the bubble at the same height as the touch
            _verticalReveal = (float)(currPoint.Y / _rootSize.Height) * VerticalRevealFactor;
            // Get the distance traveled from the point where swipe started
            var diffX = Math.Abs(currPoint.X - _swipeStartPoint.X);

            var currSwipeDir = currPoint.X > _swipeStartPoint.X ? SwipeDirection.Right : SwipeDirection.Left;

            // If the swipe has just started, then Swipe direction is not determined yet
            if (_swipeDir == SwipeDirection.None)
            {
                // Check if swipe is allowed for this swipe direction
                if ((currSwipeDir == SwipeDirection.Right &&
                        _swipeRightIndex == -1) ||
                    (currSwipeDir == SwipeDirection.Left &&
                        _swipeLeftIndex == -1))
                {
                    return;
                }

                _isSwiping = true;
                _swipeDir = currSwipeDir;

                _selIndex = _swipeDir == SwipeDirection.Left ? _swipeLeftIndex : _swipeRightIndex;
                Debug.WriteLine($"\n\tSwiping started for _selIndex = {_selIndex} in the direction {_swipeDir}");
            }
            // If the user is swiping in the opposite direction to the original swipe and has crossed the _swipeStartPoint
            else if (_swipeDir != currSwipeDir)
            {
                _isSwiping = true;
                _revealPercent = _swipeDir == SwipeDirection.Left ? SwipeRightEnd : SwipeLeftEnd;
                _currOffsetX = _swipeDir == SwipeDirection.Left ? MaxOffsetX : MinOffsetX;
                return;
            }

            // Get the index of the shape below the shape being currently swiped
            _nextShapeIndex = _selIndex > 1 ? _selIndex - 1 : -1;

            // Swipe is proceeding in the original swipe direction
            var percent = Math.Min((float)(diffX * 2 / _rootSize.Width), 1f);

            switch (_swipeDir)
            {
                case SwipeDirection.Left:
                    _revealPercent = percent;
                    _currOffsetX = Lerp(MaxOffsetX, MinOffsetX, EaseOut(percent));
                    break;
                case SwipeDirection.Right:
                    _revealPercent = 1 - percent;
                    _currOffsetX = Lerp(MinOffsetX, MaxOffsetX, EaseOut(percent));
                    break;
            }

            //// Update to the next valid index
            //_nextLeftIndex = _swipeLeftIndex;
            //_nextRightIndex = _swipeRightIndex;

        }

        private void HandlePointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPointerDown)
            {
                return;
            }

            RenderGrid.ReleasePointerCapture(e.Pointer);

            _isPointerDown = false;
            _isSwiping = false;

            _sw0.Stop();

            // If it is not a valid swipe no need to go further
            if (_swipeDir == SwipeDirection.None)
            {
                return;
            }

            var currPoint = e.GetCurrentPoint(RenderGrid).Position;
            if (currPoint.X.IsCloseTo(_swipeStartPoint.X))
            {
                return;
            }

            var currSwipeDir = currPoint.X > _swipeStartPoint.X ? SwipeDirection.Right : SwipeDirection.Left;

            if (_swipeDir != currSwipeDir)
            {
                _revealPercent = _swipeDir == SwipeDirection.Left ? SwipeRightEnd : SwipeLeftEnd;
                _currOffsetX = _swipeDir == SwipeDirection.Left ? MaxOffsetX : MinOffsetX;
            }
            else
            {
                var diffX = Math.Abs(currPoint.X - _swipeStartPoint.X);
                var swipePercent = Math.Min((float)(diffX * 2 / _rootSize.Width), 1f);

                var isValidSwipe = (swipePercent > SwipeThreshold) ||
                                   ((_sw0.ElapsedMilliseconds < SwipeThresholdDuration.TotalMilliseconds) &&
                                    ((float)diffX > SwipeDistanceThreshold));

                slideDirection = SlideDirection.RightToLeft;

                var durationPercent = isValidSwipe ? (1 - swipePercent) : swipePercent;
                _a0 = TimeSpan.FromMilliseconds(Math.Max(1, DefaultGeometryAnimDuration * durationPercent));
                _a1 = TimeSpan.FromMilliseconds(Math.Max(1, DefaultOffsetAnimDuration * swipePercent));

                switch (_swipeDir)
                {
                    case SwipeDirection.Left:
                        _startVal = swipePercent;
                        _startOffsetX = Lerp(MaxOffsetX, MinOffsetX, EaseOut(swipePercent));
                        if (isValidSwipe)
                        {
                            _endVal = SwipeLeftEnd;
                            // Update to the next valid index
                            _nextLeftIndex = _selIndex <= 2 ? -1 : _selIndex - 1;
                            _nextRightIndex = _selIndex;
                            _endOffsetX = MinOffsetX;
                            Debug.WriteLine($"Pointer Released with Valid Swipe Left and _nextLeftIndex = {_nextLeftIndex} _nextRightIndex = {_nextRightIndex}");
                        }
                        else
                        {
                            _endVal = SwipeRightEnd;
                            _endOffsetX = MaxOffsetX;
                            _nextLeftIndex = _swipeLeftIndex;
                            _nextRightIndex = _swipeRightIndex;
                            Debug.WriteLine($"Pointer Released with INVALID Swipe Left and _nextLeftIndex = {_nextLeftIndex} _nextRightIndex = {_nextRightIndex}");
                        }
                        break;
                    case SwipeDirection.Right:
                        _startVal = 1 - swipePercent;
                        _startOffsetX = Lerp(MinOffsetX, MaxOffsetX, EaseOut(swipePercent));
                        if (isValidSwipe)
                        {
                            _endVal = SwipeRightEnd;
                            // Update to the next valid index
                            _nextLeftIndex = _selIndex;
                            _nextRightIndex = _selIndex >= MaxShapeCount ? -1 : _selIndex + 1;
                            _endOffsetX = MaxOffsetX;
                            Debug.WriteLine($"Pointer Released with Valid Swipe Right and _nextLeftIndex = {_nextLeftIndex} _nextRightIndex = {_nextRightIndex}");
                        }
                        else
                        {
                            _endVal = SwipeLeftEnd;
                            _endOffsetX = MinOffsetX;
                            _nextLeftIndex = _swipeLeftIndex;
                            _nextRightIndex = _swipeRightIndex;
                            Debug.WriteLine($"Pointer Released with INVALID Swipe Right and _nextLeftIndex = {_nextLeftIndex} _nextRightIndex = {_nextRightIndex}");
                        }
                        break;
                }

                //Debug.WriteLine($"_nextRightIndex = {_nextRightIndex} _nextLeftIndex = {_nextLeftIndex}");

                _sw1.Start();
                _sw2.Start();
            }
        }
    }
}
