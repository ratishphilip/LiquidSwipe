using CompositionProToolkit;
using CompositionProToolkit.Win2d;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace LiquidSwipe
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private enum SlideDirection
        {
            None,
            LeftToRight,
            RightToLeft,
        };

        public const int MaxShapeCount = 5;

        private Compositor _c;
        private ICompositionGenerator _g;
        private ShapeVisual _rootShape;
        private ImplicitAnimationCollection _slideLeftImplicitAnimationCollection;
        private ImplicitAnimationCollection _slideRightImplicitAnimationCollection;

        private Stopwatch _sw;
        private TimeSpan _animDuration;
        private float _startVal;
        private float _endVal;

        float revealPercent;
        float verReveal;
        float waveCenterY;
        float waveHorRadius;
        float waveVertRadius;
        float sideWidth;
        SlideDirection slideDirection;
        private readonly Color[] _colors;
        private int _selIndex;
        private int _nextIndex;

        private readonly Vector2 HideOffset = new Vector2(100, 0);

        private Size _rootSize;

        public MainPage()
        {
            InitializeComponent();
            _sw = new Stopwatch();
            _sw.Reset();

            verReveal = 1f;
            _animDuration = TimeSpan.FromSeconds(1.5);
            _colors = new Color[]
            {
                Colors.Crimson,
                CanvasObject.CreateColor("#007aff"),
                CanvasObject.CreateColor("#4cd964"),
                CanvasObject.CreateColor("#ff2d55"),
                CanvasObject.CreateColor("#ff9600")
            };
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

            _slideLeftImplicitAnimationCollection = _c.CreateImplicitAnimationCollection();
            var slideLeftAnim = _c.GenerateVector2KeyFrameAnimation()
                               .HavingDuration(700)
                               .ForTarget(() => _c.CreateSpriteShape().Offset);
            slideLeftAnim.InsertFinalValueKeyFrame(1f, _c.CreateEaseInOutBackEasingFunction());
            _slideLeftImplicitAnimationCollection["Offset"] = slideLeftAnim.Animation;

            _slideRightImplicitAnimationCollection = _c.CreateImplicitAnimationCollection();
            var slideRightAnim = _c.GenerateVector2KeyFrameAnimation()
                .HavingDuration(2000)
                .ForTarget(() => _c.CreateSpriteShape().Offset);
            slideRightAnim.InsertFinalValueKeyFrame(1f, _c.CreateEaseInOutBackEasingFunction());
            _slideRightImplicitAnimationCollection["Offset"] = slideRightAnim.Animation;

            for (var i = 0; i < MaxShapeCount; i++)
            {
                var shape = _c.CreateSpriteShape(_c.CreatePathGeometry(GetClip(_rootSize)));
                shape.ImplicitAnimations = _slideLeftImplicitAnimationCollection;
                shape.Offset = HideOffset;
                shape.FillBrush = _c.CreateColorBrush(_colors[i]);
                _rootShape.Shapes.Add(shape);
            }

            _selIndex = MaxShapeCount;
            _rootShape.Shapes[_selIndex].Offset = Vector2.Zero;

            ElementCompositionPreview.SetElementChildVisual(RenderGrid, _rootShape);
            PrevBtn.IsEnabled = false;
        }

        private void OnDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
        {
            if ((!_sw.IsRunning) || (_rootShape == null))
            {
                return;
            }

            var elapsed = _sw.ElapsedMilliseconds;
            var t = (float)(elapsed / _animDuration.TotalMilliseconds);
            revealPercent = Lerp(_startVal, _endVal, EaseInOut(t));

            ((CompositionPathGeometry)((CompositionSpriteShape)_rootShape.Shapes.ElementAt(_selIndex)).Geometry).Path = GetClip(_rootSize);

            if (elapsed >= (long)_animDuration.TotalMilliseconds)
            {
                _sw.Reset();
                _selIndex = _nextIndex;
            }
        }

        private void OnPrevious(object sender, RoutedEventArgs e)
        {
            if (_selIndex == MaxShapeCount)
            {
                return;
            }

            var shape = _rootShape.Shapes.ElementAt(_selIndex);
            shape.ImplicitAnimations = _slideRightImplicitAnimationCollection;
            shape.Offset = HideOffset;

            _selIndex++;
            _nextIndex = _selIndex;

            UpdateButtonState();

            slideDirection = SlideDirection.RightToLeft;
            _startVal = 1;
            _endVal = -0.5f;
            _sw.Start();
        }

        private void OnNext(object sender, RoutedEventArgs e)
        {
            if (_selIndex == 1)
            {
                return;
            }

            if (_selIndex > 1)
            {
                var shape = _rootShape.Shapes.ElementAt(_selIndex - 1);
                shape.ImplicitAnimations = _slideLeftImplicitAnimationCollection;
                shape.Offset = Vector2.Zero;
            }

            if (_selIndex > 1)
            {
                _nextIndex = _selIndex - 1;
            }
            else
            {
                _nextIndex = _selIndex;
            }

            UpdateButtonState();
            
            slideDirection = SlideDirection.RightToLeft;
            _startVal = 0;
            _endVal = 1.1f;
            _sw.Start();
        }

        private void UpdateButtonState()
        {
            PrevBtn.IsEnabled = _nextIndex != MaxShapeCount;
            NextBtn.IsEnabled = _nextIndex != 1;
        }

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

        static float Lerp(float start_value, float end_value, float pct)
        {
            return (start_value + (end_value - start_value) * pct);
        }

        ///
        /// The following helper methods are based on
        /// https://github.com/iamSahdeep/liquid_swipe_flutter/blob/master/lib/Clippers/WaveLayer.dart 
        ///
        CompositionPath GetClip(Size size)
        {
            CanvasPathBuilder pB = new CanvasPathBuilder(_g.Device);
            sideWidth = GetSideWidth(size);
            waveVertRadius = GetWaveVerticalRadiusF(size);

            waveCenterY = (float)(size.Height * (2 * verReveal / 3));
            waveHorRadius = slideDirection == SlideDirection.LeftToRight
                ? GetWaveHorizontalRadiusFBack(size)
                : GetWaveHorizontalRadiusF(size);

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

        float GetSideWidth(Size size)
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

        float GetWaveVerticalRadiusF(Size size)
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

        float GetWaveHorizontalRadiusF(Size size)
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

        float GetWaveHorizontalRadiusFBack(Size size)
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
    }
}
