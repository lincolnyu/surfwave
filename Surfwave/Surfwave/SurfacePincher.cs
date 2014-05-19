using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Surfwave
{
    public class SurfacePincher
    {
        #region Delegates

        public delegate void TransformStagedEvent(bool justStarted);

        /// <summary>
        ///  Event fired when the class interprets the current pinch gesture to a transform
        /// </summary>
        /// <param name="k1">The ratio of horizontal expansion</param>
        /// <param name="k2">The ratio of vertical expasion</param>
        /// <param name="d1">Horizontal offset</param>
        /// <param name="d2">Vertical offset</param>
        public delegate void TransformChangedEvent(double k1, double k2, double d1, double d2);

        #endregion
        #region Nested types


        public class PointerInfo
        {
            public Point StartingPosition;
            public Point CurrentPosition;
        }

        private class Transform
        {
            #region Fields

            public double Kx;
            public double Ky;
            public double Dx;
            public double Dy;

            #endregion

            #region Constructors

            public Transform()
            {
                Reset();
            }

            #endregion

            #region Methods

            public void Reset()
            {
                Kx = Ky = 1;
                Dx = Dy = 0;
            }

            /// <summary>
            ///  applies <paramref name="other"/> onto this (prependingly)
            /// </summary>
            /// <param name="other">The </param>
            public void Prepend(Transform other)
            {
                var newKx = other.Kx * Kx;
                var newKy = other.Ky * Ky;
                var newDx = other.Kx * Dx + other.Dx;
                var newDy = other.Ky * Dy + other.Dy;
                Kx = newKx;
                Ky = newKy;
                Dx = newDx;
                Dy = newDy;
            }

            public void Append(Transform other)
            {
                var newKx = Kx * other.Kx;
                var newKy = Ky * other.Ky;
                var newDx = Kx * other.Dx + Dx;
                var newDy = Ky * other.Dy + Dy;
                Kx = newKx;
                Ky = newKy;
                Dx = newDx;
                Dy = newDy;
            }

            #endregion
        }

        #endregion

        #region Fields

        /// <summary>
        ///  Backing field for SurfaceElement
        /// </summary>
        private UIElement _surfaceElement;

        private readonly bool _retainAspectRatio;

        private readonly Dictionary<uint, PointerInfo> _pressedPointers = new Dictionary<uint, PointerInfo>();

        private readonly Transform _baseTransform = new Transform();

        private bool _activated;

        #endregion

        #region Constructors

        public SurfacePincher(bool retainAspectRation = true)
        {
            _retainAspectRatio = retainAspectRation;
            PreviousCount = CurrentCount = 0;
        }

        ~SurfacePincher()
        {
            Deactivate();
        }

        #endregion

        #region Properties

        public int PreviousCount { get; private set; }

        /// <summary>
        ///  Current number of points pressed
        /// </summary>
        public int CurrentCount { get; private set; }

        public bool Activated
        {
            get
            {
                return _activated;
            }
            set
            {
                if (_activated != value)
                {
                    if (value)
                    {
                        Activate();
                    }
                    else
                    {
                        Deactivate();
                    }
                }
            }
        }

        public bool Frozen
        {
            get; set;
        }

        /// <summary>
        ///  The element the pincher is working on, typically a canvas
        /// </summary>
        public UIElement SurfaceElement
        {
            get
            {
                return _surfaceElement;
            }
            set
            {
                if (!Equals(_surfaceElement, value))
                {
                    var wasActivated = Activated;
                    Deactivate();
                    _surfaceElement = value;
                    if (wasActivated)
                    {
                        Activate();
                    }
                }
            }
        }

        /// <summary>
        ///  pressed pointers and their corresponding ifno items exposed the caller
        /// </summary>
        public IReadOnlyDictionary<uint, PointerInfo> PressedPointers
        {
            get
            {
                return _pressedPointers;
            }
        }

        public InputFilter InputFilter
        {
            get; set;
        }

        #endregion

        #region Events

        public event TransformChangedEvent TransformChanged;

        public event TransformStagedEvent TransformStaged;

        #endregion

        #region Methods

        private void Activate()
        {
            if (SurfaceElement != null && !_activated)
            {
                SurfaceElement.PointerPressed += OnPointerPressed;
                SurfaceElement.PointerMoved += OnPointerMoved;
                SurfaceElement.PointerReleased += OnPointerReleased;
                SurfaceElement.PointerExited += OnPointerExited;
                _activated = true;
            }
        }

        private void Deactivate()
        {
            if (SurfaceElement != null && _activated)
            {
                SurfaceElement.PointerPressed -= OnPointerPressed;
                SurfaceElement.PointerMoved -= OnPointerMoved;
                SurfaceElement.PointerReleased -= OnPointerReleased;
                SurfaceElement.PointerExited -= OnPointerExited;
                _activated = false;
            }
            _pressedPointers.Clear();
            PreviousCount = CurrentCount = 0;
            ResetTransform();
        }

        /// <summary>
        ///  
        /// </summary>
        public void ResetTransform()
        {
            _baseTransform.Reset();
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!Activated || InputFilter != null && !InputFilter(SurfaceElement, sender, e))
            {
                return;
            }

#if !MOUSE_EMULATED_TOUCH
            var pointerDevType = e.Pointer.PointerDeviceType;

            // we may also allow other device input which mimicks finger touch?
            if (pointerDevType != PointerDeviceType.Touch)
            {
                return;
            }
#endif

            var id = e.Pointer.PointerId;
            var currentPoint = e.GetCurrentPoint(_surfaceElement);
            var currentPosition = currentPoint.Position;

            ActionPointerPressed(id, currentPosition);

            e.Handled = true;
        }

        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!Activated)
            {
                return;
            }

            var id = e.Pointer.PointerId;
            var currentPoint = e.GetCurrentPoint(_surfaceElement);
            var currentPosition = currentPoint.Position;

            ActionPointerMoved(id, currentPosition);

            e.Handled = true;
        }

        private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!Activated)
            {
                return;
            }

            var id = e.Pointer.PointerId;

            ActionPointerReleased(id);

            e.Handled = true;
        }

        private void OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!Activated)
            {
                return;
            }

            OnPointerReleased(sender, e);
        }

        public static bool DisableAllButRButtonFilter(UIElement surfaceElement, object sender, PointerRoutedEventArgs e)
        {
            var pointerDevType = e.Pointer.PointerDeviceType;
            if (pointerDevType != PointerDeviceType.Mouse)
            {
                return false;
            }
            var currentPoint = e.GetCurrentPoint(surfaceElement);
            var isRightButtonPressed = currentPoint.Properties.IsRightButtonPressed;
            if (!isRightButtonPressed)
            {
                return false;
            }
            return true;
        }

        private void ActionPointerPressed(uint id, Point currentPosition)
        {
            var ptinfo = new PointerInfo
            {
                StartingPosition = currentPosition,
                CurrentPosition = currentPosition
            };

            PreviousCount = _pressedPointers.Count;

            _pressedPointers[id] = ptinfo;

            CurrentCount = _pressedPointers.Count;

            if (PreviousCount == 0)
            {
                BeginTouch();
            }
            else
            {
                StageTouch();
            }
        }

        private void ActionPointerReleased(uint id)
        {
            PreviousCount = _pressedPointers.Count;

            if (_pressedPointers.ContainsKey(id))
            {
                _pressedPointers.Remove(id);
            }

            CurrentCount = _pressedPointers.Count;

            StageTouch();
        }

        private void ActionPointerMoved(uint id, Point currentPosition)
        {
            if (Frozen)
            {
                return;
            }
            if (!_pressedPointers.ContainsKey(id))
            {
                return;
            }
            var ptinfo = _pressedPointers[id];
            ptinfo.CurrentPosition = currentPosition;

            UpdateTransform();
        }


        /// <summary>
        ///  This is called 
        /// </summary>
        private void BeginTouch()
        {
            ResetTransform();

            // udpate the starting points (not quite necessary)
            foreach (var ptinfo in _pressedPointers.Values)
            {
                ptinfo.StartingPosition = ptinfo.CurrentPosition;
            }

            // notifies the user of the staging
            if (TransformStaged != null)
            {
                TransformStaged(true);
            }
        }

        /// <summary>
        ///  This is called every time finger contact has changed and therefore the current transformation
        ///  needs to be solidfied
        /// </summary>
        private void StageTouch()
        {
            // gets the transformation from the last check stage
            double kx, ky, dx, dy;
            if (_retainAspectRatio)
            {
                GetTransformFixedAspectRatio(out kx, out dx, out dy);
                ky = kx;
            }
            else
            {
                GetTransformVarialbeAspectRatio(out kx, out ky, out dx, out dy);
            }

            // stacks the transformation onto the base transformation
            _baseTransform.Prepend(new Transform { Kx = kx, Ky = ky, Dx = dx, Dy = dy });

            // udpate the starting points
            foreach (var ptinfo in _pressedPointers.Values)
            {
                ptinfo.StartingPosition = ptinfo.CurrentPosition;
            }

            // notifies the user of the staging
            if (TransformStaged != null)
            {
                TransformStaged(false);
            }
        }

        private void UpdateTransform()
        {
            if (TransformChanged == null)
            {
                return;
            }

            double kx = 1, ky = 1, dx = 0, dy = 0;
            if (_pressedPointers.Count > 1)
            {
                if (_retainAspectRatio)
                {
                    GetTransformFixedAspectRatio(out kx, out dx, out dy);
                    ky = kx;
                }
                else
                {
                    GetTransformVarialbeAspectRatio(out kx, out ky, out dx, out dy);
                }
            }
            else if (_pressedPointers.Count == 1)
            {
                GetTranslation(out dx, out dy);
            }

            var nt = new Transform { Kx = kx, Ky = ky, Dx = dx, Dy = dy };
            nt.Append(_baseTransform);
            TransformChanged(nt.Kx, nt.Ky, nt.Dx, nt.Dy);
        }

        private void GetTransformFixedAspectRatio(out double k, out double dx, out double dy)
        {
            var sumxs = 0.0;
            var sumys = 0.0;
            var sumxc = 0.0;
            var sumyc = 0.0;
            var sumxsxc = 0.0;
            var sumysyc = 0.0;
            var sumxxs = 0.0;
            var sumyys = 0.0;

            foreach (var p in _pressedPointers.Values)
            {
                var startPos = p.StartingPosition;
                var currPos = p.CurrentPosition;

                sumxs += startPos.X;
                sumys += startPos.Y;

                sumxc += currPos.X;
                sumyc += currPos.Y;

                sumxsxc += startPos.X * currPos.X;
                sumysyc += startPos.Y * currPos.Y;

                sumxxs += startPos.X * startPos.X;
                sumyys += startPos.Y * startPos.Y;
            }
            var a11 = sumxxs + sumyys;
            var a12 = sumxs;
            var a13 = sumys;
            var a21 = sumxs;
            var a22 = _pressedPointers.Count;
            var a31 = sumys;
            var a33 = _pressedPointers.Count;
            var b1 = sumxsxc + sumysyc;
            var b2 = sumxc;
            var b3 = sumyc;

            var delta = a11 * a22 - a12 * a21;
            var delta2 = a33 * delta - a13 * a31 * a22;

            if (Math.Abs(delta) < double.Epsilon || Math.Abs(delta2) < double.Epsilon ||
                Math.Abs(a21) < double.Epsilon || Math.Abs(a11) < double.Epsilon)
            {
                k = 1;
                dx = 0;
                dy = 0;
                return;
            }

            dy = b3 * a21 - b2 * a31;
            dy *= delta;
            dy += (a11 * b2 - a21 * b1) * a22 * a31;
            dy /= a21;
            dy /= delta2;

            dx = b2 * a11 - a21 * b1;
            dx += dy * a21 * a13;
            dx /= delta;

            k = b1;
            k -= a12 * dx + a13 * dy;
            k /= a11;
        }

        private void GetTransformVarialbeAspectRatio(out double kx, out double ky, out double dx, out double dy)
        {
            var sumxsxs = 0.0;
            var sumxs = 0.0;
            var sumxc = 0.0;
            var sumxsxc = 0.0;

            var sumysys = 0.0;
            var sumys = 0.0;
            var sumyc = 0.0;
            var sumysyc = 0.0;

            foreach (var p in _pressedPointers.Values)
            {
                var startPos = p.StartingPosition;
                var currPos = p.CurrentPosition;

                sumxsxs += startPos.X * startPos.X;
                sumxs += startPos.X;
                sumxc += currPos.X;
                sumxsxc += startPos.X * currPos.X;

                sumysys += startPos.Y * startPos.Y;
                sumys += startPos.Y;
                sumyc += currPos.Y;
                sumysyc += startPos.Y * currPos.Y;
            }

            var n = _pressedPointers.Count;

            SolveMatrix2d(sumxsxs, sumxs, sumxs, n, sumxsxc, sumxc, out kx, out dx);
            SolveMatrix2d(sumysys, sumys, sumys, n, sumysyc, sumyc, out ky, out dy);
        }

        private void GetTranslation(out double dx, out double dy)
        {
            var p = _pressedPointers.Values.First();
            var startPos = p.StartingPosition;
            var currPos = p.CurrentPosition;

            dx = currPos.X - startPos.X;
            dy = currPos.Y - startPos.Y;
        }

        // ReSharper disable once UnusedMethodReturnValue.Local
        private bool SolveMatrix2d(double a11, double a12, double a21, double a22, double b1, double b2,
            out double k, out double d)
        {
            var delta = a22 * a11 - a12 * 21;
            if (Math.Abs(delta) < double.Epsilon)
            {
                k = 1;
                d = 0;
                return false;
            }
            d = b2 * a11 - b1 * a21;
            d /= delta;

            k = b1 - a12 * d;
            return true;
        }

        #endregion

        #region Testing Methods

        // To test simply bind this method to an event easy to invoke such as DoubleTapped
        // ReSharper disable once UnusedMember.Local
        // ReSharper disable UnusedParameter.Local
        private void RunTestPinchOut(object sender, DoubleTappedRoutedEventArgs e)
        // ReSharper restore UnusedParameter.Local
        {
            TestPinchOut();
        }

        private async void TestPinchOut()
        {
            var canvasWidth = ((Canvas)_surfaceElement).ActualWidth;
            var canvasHeight = ((Canvas)_surfaceElement).ActualHeight;
            var pinchSrc1 = new Point(canvasWidth / 4, canvasHeight / 3);
            var pinchSrc2 = new Point(canvasWidth / 2, canvasHeight / 5);
            var pinchSrc3 = new Point(canvasWidth / 3, canvasHeight * 4 / 5);

            var pinchDst1 = new Point(canvasWidth / 3, canvasHeight / 2);
            var pinchDst2 = new Point(canvasWidth * 0.45, canvasHeight * 0.45);
            var pinchDst3 = new Point(canvasWidth * 0.40, canvasHeight * 0.70);

            ActionPointerPressed(11, pinchSrc1);
            await Task.Delay(100);
            ActionPointerPressed(12, pinchSrc2);
            await Task.Delay(100);
            ActionPointerPressed(13, pinchSrc3);
            const int intervals = 100;
            for (var i = 1; i <= intervals; i++)
            {
                var r = (double)i / intervals;
                var pinchCurr1 = new Point(pinchSrc1.X * (1 - r) + pinchDst1.X * r, pinchSrc1.Y * (1 - r) + pinchDst1.Y * r);
                var pinchCurr2 = new Point(pinchSrc2.X * (1 - r) + pinchDst2.X * r, pinchSrc2.Y * (1 - r) + pinchDst2.Y * r);
                var pinchCurr3 = new Point(pinchSrc3.X * (1 - r) + pinchDst3.X * r, pinchSrc3.Y * (1 - r) + pinchDst3.Y * r);
                ActionPointerMoved(11, pinchCurr1);
                ActionPointerMoved(12, pinchCurr2);
                ActionPointerMoved(13, pinchCurr3);
                await Task.Delay(3);
            }

            ActionPointerReleased(11);
            ActionPointerReleased(12);
            ActionPointerReleased(13);
        }

        #endregion
    }
}
