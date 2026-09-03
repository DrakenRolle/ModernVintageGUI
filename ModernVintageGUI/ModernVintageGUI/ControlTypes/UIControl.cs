using Cairo;
using IS2Mod.ControlTypes.Custom;
using IS2Mod.ControlTypes.Events;
using IS2Mod.Enums;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Vintagestory.API.Client;
using Vintagestory.API.Util;

namespace IS2Mod.ControlTypes
{
    public abstract class UIControl : INotifyPropertyChanged
    {
        #region Events
        public event PropertyChangedEventHandler? PropertyChanged;

        public event EventHandler<MouseEventArgs>? Clicked;
        public event EventHandler<MouseEventArgs>? Enter;
        public event EventHandler<MouseEventArgs>? Exit;
        public event EventHandler<MouseEventArgs>? MouseDown;
        public event EventHandler<MouseEventArgs>? MouseUp;
        public event EventHandler<MouseEventArgs>? MouseMove;

        public event EventHandler<Events.MouseWheelEventArgs>? MouseWheel;

        public void InvokeEventClicked(MouseEvent vsArgs)
        {
            var args = new MouseEventArgs(vsArgs);
            Clicked?.Invoke(this, args);
        }

        public void InvokeEventMouseMove(MouseEvent vsArgs)
        {
            var args = new MouseEventArgs(vsArgs);
            MouseMove?.Invoke(this, args);
        }
        public void InvokeEventEnter(MouseEvent vsArgs)
        {
            var args = new MouseEventArgs(vsArgs);
            Enter?.Invoke(this, args);
        }
        public void InvokeEventExit(MouseEvent vsArgs)
        {
            var args = new MouseEventArgs(vsArgs);
            Exit?.Invoke(this, args);
        }
        public void InvokeEventMouseDown(MouseEvent vsArgs)
        {
            var args = new MouseEventArgs(vsArgs);
            MouseDown?.Invoke(this, args);
        }
        public void InvokeEventMouseUp(MouseEvent vsArgs)
        {
            var args = new MouseEventArgs(vsArgs);
            MouseUp?.Invoke(this, args);
        }
        public void InvokeEventMouseWheel(Vintagestory.API.Client.MouseWheelEventArgs vsArgs)
        {
            var args = new Events.MouseWheelEventArgs(vsArgs);
            MouseWheel?.Invoke(this, args);
        }

        #endregion

        #region Properties
        private LoadedTexture? _staticElementsTexture;
        public LoadedTexture? StaticElementsTexture
        {
            get => _staticElementsTexture;
            set => _staticElementsTexture = value;
        }

        private ObservableCollection<UIControl> _children = new ObservableCollection<UIControl>();
        public ObservableCollection<UIControl> Children
        {
            get => _children;
            set => _children = value;
        }

        private UIControl? _parent;
        public UIControl? Parent
        {
            get => _parent;
            set => SetProperty(ref _parent, value);
        }

        private CustomDialogElement? _dialog;
        /// <summary>
        /// The dialog this control belongs to, or null while the control is still detached.
        /// Building a subtree before adding it to a dialog is a normal usage pattern, so this
        /// returns null instead of throwing.
        /// </summary>
        public CustomDialogElement? Dialog
        {
            get
            {
                if (_parent != null)
                    return _parent.Dialog;

                return _dialog;
            }
            set => _dialog = value;
        }

        private PointD _position;
        public PointD Position
        {
            get => _position;
            set => SetProperty(ref _position, value);
        }

        private PointD _size = new PointD(0, 0);
        private PointD _explicitSize = new PointD(0, 0);

        /// <summary>
        /// The size this control currently occupies. Assigning it from outside declares a
        /// wanted size (see <see cref="ExplicitSize"/>); the layout passes write it through
        /// <see cref="SetLayoutSize"/> instead so that they do not overwrite that wish.
        /// </summary>
        public PointD Size
        {
            get => _size;
            set
            {
                _explicitSize = value;
                _size = value;
            }
        }

        /// <summary>
        /// The size that was last assigned from outside. This is the input of the measure pass
        /// for controls with <see cref="IsAutoSize"/> = false and must never be written by the
        /// layout itself - otherwise measuring would consume its own previous output and the
        /// control would drift with every layout pass.
        /// </summary>
        protected PointD ExplicitSize => _explicitSize;

        /// <summary>
        /// Size assignment for the layout passes. Unlike the <see cref="Size"/> setter this
        /// leaves <see cref="ExplicitSize"/> alone, so stretching and clipping stay repeatable.
        /// </summary>
        protected internal void SetLayoutSize(PointD size)
        {
            _size = size;
        }

        private double _layoutScale = 1.0;

        /// <summary>
        /// Device pixels per author unit, the same idea as GuiElement.scaled() in the vanilla
        /// GUI: everything a caller specifies - Margin, Padding, Size, FontSize, BorderWidth -
        /// is written in unscaled author units, and the layout multiplies by this on the way to
        /// device pixels. Position, Size and CalculatedSize are therefore already device pixels,
        /// which is what the renderer and the hit test need (mouse coordinates are device
        /// pixels too), so nothing has to be transformed back.
        ///
        /// Only the value on the root of a tree matters; every control reports the root value.
        /// <see cref="Custom.CustomDialogElement"/> keeps it in sync with RuntimeEnv.GUIScale,
        /// the layout harness sets it by hand to render a scenario at several scales.
        /// </summary>
        public double LayoutScale
        {
            get => Parent?.LayoutScale ?? _layoutScale;
            set => _layoutScale = value;
        }

        /// <summary>Margin in device pixels.</summary>
        protected double ScaledMargin => Margin * LayoutScale;

        /// <summary>Padding in device pixels.</summary>
        protected double ScaledPadding => Padding * LayoutScale;

        /// <summary>The size assigned from outside, in device pixels.</summary>
        protected PointD ScaledExplicitSize => new PointD(
            ExplicitSize.X * LayoutScale,
            ExplicitSize.Y * LayoutScale);

        private bool _isAutoSize;
        public bool IsAutoSize
        {
            get => _isAutoSize;
            set => SetProperty(ref _isAutoSize, value);
        }

        private double _margin;
        public double Margin
        {
            get => _margin;
            set => SetProperty(ref _margin, value);
        }

        private double _padding;
        public double Padding
        {
            get => _padding;
            set => SetProperty(ref _padding, value);
        }

        private int _index;
        public int Index
        {
            get => _index;
            set => SetProperty(ref _index, value);
        }

        private Orientation _insideOrientation;
        public Orientation InsideOrientation
        {
            get => _insideOrientation;
            set => SetProperty(ref _insideOrientation, value);
        }

        private Orientation _orientation;
        public Orientation Orientation
        {
            get => _orientation;
            set => SetProperty(ref _orientation, value);
        }

        private string _name;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private bool _isStaticElement;
        public bool IsStaticElement
        {
            get => _isStaticElement;
            set => SetProperty(ref _isStaticElement, value);
        }

        // Store the actual size before any clipping
        private PointD _calculatedSize;
        protected PointD CalculatedSize
        {
            get => _calculatedSize;
            set => _calculatedSize = value;
        }
        #endregion

        #region Constructors
        protected UIControl(
            string _Name = "",
            PointD? _Size = null,
            Orientation _Orientation = Orientation.None,
            double _Margin = 0,
            double _Padding = 0,
            int _Index = 0)
        {
            _name = _Name;
            _margin = _Margin;
            _padding = _Padding;
            _index = _Index;
            _insideOrientation = _Orientation;
            _orientation = Orientation.Top; // Default child orientation

            if (_Size.HasValue)
            {
                _size = _Size.Value;
                _explicitSize = _Size.Value;
                _isAutoSize = _size.X == 0 && _size.Y == 0;
            }
            else
            {
                _isAutoSize = true;
            }

            _calculatedSize = _size;
        }
        #endregion

        #region Rendering
        /// <summary>
        /// Draws this control and all of its children onto the shared Cairo surface of the
        /// dialog. Do not upload anything to the GPU here - the dialog uploads the finished
        /// surface exactly once per refresh in <see cref="CustomDialogElement.RenderDialog"/>.
        /// </summary>
        public virtual void GenerateRenderData(ImageSurface surface, Context context)
        {
            foreach (var child in Children)
            {
                child.GenerateRenderData(surface, context);
            }
        }
        #endregion

        #region Layout
        /// <summary>
        /// Runs a full layout pass over this control and everything below it: measure, then
        /// arrange (stretch, position, clip). Call this on the root of a tree.
        ///
        /// The pass is idempotent - running it twice on an unchanged tree has to produce the
        /// same result. LayoutHarness verifies that; if you add a control, keep it that way:
        /// measure must read <see cref="ExplicitSize"/>, never <see cref="Size"/>, and arrange
        /// must write through <see cref="SetLayoutSize"/>, never through the Size setter.
        /// </summary>
        public virtual void PerformLayout()
        {
            CalculateChildrenRelationship();
            CalculateSize();
            NormalizeChildrenByDelta();
            CalculateAllPositions();
        }
        #endregion

        #region Hierarchy Management
        public void CalculateChildrenRelationship()
        {
            foreach (UIControl child in Children)
            {
                child._parent = this;
                child._dialog = this.Dialog;

                child.CalculateChildrenRelationship();
            }

            // Detach first: this method runs on every Show(), and subscribing again without
            // unsubscribing would fire the handler once per previous Show().
            Children.CollectionChanged -= Children_CollectionChanged;
            Children.CollectionChanged += Children_CollectionChanged;
        }

        private void Children_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (UIControl control in e.NewItems)
                {
                    if (control != null)
                    {
                        control.Parent = this;
                        control._dialog = this.Dialog;
                    }
                }
            }

            if (e.OldItems != null)
            {
                foreach (UIControl control in e.OldItems)
                {
                    if (control != null)
                    {
                        control.Parent = null;
                        control._dialog = null;
                    }
                }
            }

            RecomposeToMain();
        }

        /// <summary>
        /// Re-runs layout and redraws the dialog. Sizes alone are not enough - positions and
        /// the normalization pass depend on them, so a partial update would leave the tree in
        /// an inconsistent state.
        /// </summary>
        public void RecomposeToMain()
        {
            CustomDialogElement? dialog = Dialog;
            if (dialog == null || !dialog.IsVisible)
                return;

            dialog.PerformLayout();
            dialog.Refresh();
        }
        #endregion

        #region Size Calculation
        /// <summary>
        /// Calculates the size of this control based on its children and settings.
        /// Does NOT apply clipping - that happens in position calculation.
        /// </summary>
        public virtual PointD CalculateSize()
        {
            // Children are measured in both branches - the arrange pass needs their sizes even
            // when they do not influence the size of this control.
            PointD content = new PointD(0, 0);

            foreach (UIControl child in Children)
            {
                PointD childSize = child.CalculateSize();
                PointD childSizeWithSpacing = GetChildSizeWithSpacing(child, childSize);
                content = MergeSizeByOrientation(childSizeWithSpacing, content);
            }

            // A fixed-size control measures to exactly the size it was given. Reading the
            // current Size here instead of ExplicitSize is what used to make repeated layout
            // passes grow the control: Size is also the output of the arrange pass, so every
            // run folded the previous result plus the children plus the padding back in.
            PointD measured = IsAutoSize
                ? new PointD(content.X + ScaledPadding * 2, content.Y + ScaledPadding * 2)
                : ScaledExplicitSize;

            _calculatedSize = measured;

            // Give the control a usable size right away. The arrange pass (normalization and
            // clipping) refines it afterwards through SetLayoutSize.
            SetLayoutSize(measured);

            return measured;
        }


        /// <summary>
        /// Normalizes children sizes based on delta division of parent's available space.
        /// - Top/Bottom orientation: All children get parent's content width
        /// - Left/Right orientation: All children get parent's content height
        /// - None orientation: No normalization
        /// This is applied recursively to all descendants.
        /// </summary>
        public virtual void NormalizeChildrenByDelta()
        {
            if (Children.Count == 0)
                return;

            // Calculate available content area (parent size minus padding)
            double availableWidth = Size.X - (ScaledPadding * 2);
            double availableHeight = Size.Y - (ScaledPadding * 2);

            switch (InsideOrientation)
            {
                case Orientation.Top:
                case Orientation.Bottom:
                    // Vertical stacking: normalize width across all children
                    NormalizeChildrenWidth(availableWidth);
                    break;
                case Orientation.Left:
                case Orientation.Right:
                    // Horizontal stacking: normalize height across all children
                    NormalizeChildrenHeight(availableHeight);
                    break;
                case Orientation.None:
                    // No normalization for overlay mode
                    break;
            }

            // Recursively normalize all descendants
            foreach (UIControl child in Children)
            {
                child.NormalizeChildrenByDelta();
            }
        }

        /// <summary>
        /// Normalizes all children to have the same width based on parent's available content width.
        /// Accounts for each child's margin when distributing space.
        /// </summary>
        private void NormalizeChildrenWidth(double availableWidth)
        {
            foreach (UIControl child in Children)
            {
                // Calculate width available for this child (subtract child's margins)
                double childAvailableWidth = availableWidth - (child.ScaledMargin * 2);

                // Ensure we don't set negative or zero width
                childAvailableWidth = Math.Max(1, childAvailableWidth);

                // Stretching is part of arranging, so it goes through SetLayoutSize and
                // leaves the measured size (_calculatedSize) untouched - overwriting that
                // would destroy the natural size the next measure pass builds on.
                child.SetLayoutSize(new PointD(childAvailableWidth, child.Size.Y));
                if (child.Children.Count > 0)
                {
                    child.NormalizeChildrenByDelta();
                }
            }
        }

        /// <summary>
        /// Normalizes all children to have the same height based on parent's available content height.
        /// Accounts for each child's margin when distributing space.
        /// </summary>
        private void NormalizeChildrenHeight(double availableHeight)
        {
            foreach (UIControl child in Children)
            {
                // Calculate height available for this child (subtract child's margins)
                double childAvailableHeight = availableHeight - (child.ScaledMargin * 2);

                // Ensure we don't set negative or zero height
                childAvailableHeight = Math.Max(1, childAvailableHeight);

                // Same as above: arrange writes the layout size, not the measured size.
                child.SetLayoutSize(new PointD(child.Size.X, childAvailableHeight));
                if (child.Children.Count > 0)
                {
                    child.NormalizeChildrenByDelta();
                }
            }
        }

        /// <summary>
        /// Adds margin and padding to a child's size for layout calculations.
        /// </summary>
        private PointD GetChildSizeWithSpacing(UIControl child, PointD childSize)
        {
            double totalMargin = 2 * child.ScaledMargin;
            //double totalPadding = 2 * this.Padding;

            return new PointD(
                childSize.X + totalMargin /*+ totalPadding*/,
                childSize.Y + totalMargin /*+ totalPadding*/
            );
        }

        /// <summary>
        /// Merges child size into current size based on orientation.
        /// - Top/Bottom: Stack vertically (add heights, take max width)
        /// - Left/Right: Stack horizontally (add widths, take max height)
        /// - None: Overlay (take max of both)
        /// </summary>
        private PointD MergeSizeByOrientation(PointD childSize, PointD currentSize)
        {
            switch (InsideOrientation)
            {
                case Orientation.Top:
                case Orientation.Bottom:
                    return new PointD(
                        Math.Max(currentSize.X, childSize.X),
                        currentSize.Y + childSize.Y
                    );

                case Orientation.Left:
                case Orientation.Right:
                    return new PointD(
                        currentSize.X + childSize.X,
                        Math.Max(currentSize.Y, childSize.Y)
                    );

                case Orientation.None:
                default:
                    return new PointD(
                        Math.Max(currentSize.X, childSize.X),
                        Math.Max(currentSize.Y, childSize.Y)
                    );
            }
        }
        #endregion

        #region Position Calculation
        /// <summary>
        /// Calculates positions for this control and all its children.
        /// </summary>
        public virtual void CalculateAllPositions()
        {
            // Root element starts at origin
            if (Parent == null)
            {
                Position = new PointD(0, 0);
            }

            // Calculate positions for all children
            for (int i = 0; i < Children.Count; i++)
            {
                UIControl? previousSibling = i > 0 ? Children[i - 1] : null;
                Children[i].CalculatePosition(previousSibling);
                Children[i].CalculateAllPositions();
            }

            // Normalize children after positions are calculated
            NormalizeChildrenByDelta();
        }

        /// <summary>
        /// FIXED: Calculates the position of this control relative to its parent and siblings.
        /// Now correctly handles Orientation.Right to position on the right side.
        /// </summary>
        public void CalculatePosition(UIControl? previousSibling)
        {
            if (Parent == null)
            {
                Position = new PointD(0, 0);
                return;
            }

            // Calculate base position with parent padding and own margin
            double posX = Parent.Position.X + Parent.ScaledPadding + ScaledMargin;
            double posY = Parent.Position.Y + Parent.ScaledPadding + ScaledMargin;

            // FIXED: Handle Orientation.Right - position from the right edge
            if (Orientation == Orientation.Right)
            {
                posX = Parent.Position.X + Parent.Size.X - Size.X - ScaledMargin - Parent.ScaledPadding;
            }

            // FIXED: Handle Orientation.Bottom - position from the bottom edge
            if (Orientation == Orientation.Bottom)
            {
                posY = Parent.Position.Y + Parent.Size.Y - Size.Y - ScaledMargin - Parent.ScaledPadding;
            }

            // Adjust position based on previous sibling and parent orientation
            if (previousSibling != null)
            {
                switch (Parent.InsideOrientation)
                {
                    case Orientation.Top:
                    case Orientation.Bottom:
                        // Stack vertically - keep X, add to Y
                        posY = previousSibling.Position.Y + previousSibling.Size.Y + previousSibling.ScaledMargin + ScaledMargin;
                        break;

                    case Orientation.Left:
                    case Orientation.Right:
                        // Stack horizontally - add to X, keep Y
                        posX = previousSibling.Position.X + previousSibling.Size.X + previousSibling.ScaledMargin + ScaledMargin;
                        break;
                    case Orientation.None:
                        // Overlay - use parent position (already set above)
                        break;
                }
            }

            // Apply clipping if control extends beyond parent bounds
            PointD clippedSize = CalculateClippedSize(posX, posY);

            Position = new PointD(posX, posY);

            // Clipping is a layout result, not a wish - going through the Size setter here
            // would turn the clipped value into the ExplicitSize that the next measure pass
            // starts from, and the control would shrink a little more on every pass.
            SetLayoutSize(clippedSize);
        }

        /// <summary>
        /// Calculates the clipped size when the control extends beyond parent bounds.
        /// </summary>
        private PointD CalculateClippedSize(double proposedX, double proposedY)
        {
            if (Parent == null)
            {
                return _calculatedSize;
            }

            // Parent boundaries (accounting for padding)
            double parentMinX = Parent.Position.X + Parent.ScaledPadding;
            double parentMinY = Parent.Position.Y + Parent.ScaledPadding;
            double parentMaxX = Parent.Position.X + Parent.Size.X - Parent.ScaledPadding;
            double parentMaxY = Parent.Position.Y + Parent.Size.Y - Parent.ScaledPadding;

            // Start from the size the control actually has after normalization. The overflow
            // test used to look at _calculatedSize while clamping Size, so it asked about one
            // box and cut a different one.
            double clippedWidth = Size.X;
            double clippedHeight = Size.Y;

            // Only clip if control starts outside bounds or extends significantly beyond
            // Don't clip right edge if control fits within reasonable margin tolerance
            if (proposedX + clippedWidth > parentMaxX + ScaledMargin)
            {
                clippedWidth = Math.Max(0, parentMaxX - proposedX);
            }

            // Only clip bottom edge if control extends significantly beyond
            if (proposedY + clippedHeight > parentMaxY + ScaledMargin)
            {
                clippedHeight = Math.Max(0, parentMaxY - proposedY);
            }

            // Clip left edge (if positioned before parent content area)
            if (proposedX < parentMinX)
            {
                clippedWidth = Math.Max(0, clippedWidth - (parentMinX - proposedX));
            }

            // Clip top edge (if positioned before parent content area)
            if (proposedY < parentMinY)
            {
                clippedHeight = Math.Max(0, clippedHeight - (parentMinY - proposedY));
            }

            return new PointD(clippedWidth, clippedHeight);
        }
        #endregion

        #region Hit Detection
        /// <summary>
        /// Performs hit testing to find which control is at the given screen coordinates.
        /// Returns null if the click is outside the dialog or no control is found.
        /// </summary>
        /// <param name="screenX">Screen X coordinate</param>
        /// <param name="screenY">Screen Y coordinate</param>
        /// <returns>The deepest control at the given position, or null if none found</returns>
        protected UIControl? HitTest(int screenX, int screenY)
        {
            // Only the root (the dialog) knows where the tree sits on screen. Everything below
            // it was laid out in dialog local space, so convert once here and stay local.
            double localX = screenX - Position.X;
            double localY = screenY - Position.Y;

            if (!ContainsLocalPoint(localX, localY))
            {
                return null;
            }

            return HitTestRecursive(this, localX, localY);
        }

        /// <summary>
        /// Checks whether a point given in dialog local coordinates lies inside this control.
        /// The root of the tree is the exception: its Position holds the on screen position of
        /// the whole dialog, while its own local rectangle always starts at 0/0.
        /// </summary>
        public bool ContainsLocalPoint(double localX, double localY)
        {
            double left = Parent == null ? 0 : Position.X;
            double top = Parent == null ? 0 : Position.Y;

            return localX >= left &&
                   localX <= left + Size.X &&
                   localY >= top &&
                   localY <= top + Size.Y;
        }

        /// <summary>
        /// Recursively searches the control hierarchy for the deepest control at the given
        /// dialog local position. Children are tested last to first so that controls drawn on
        /// top of their siblings also win the hit test.
        /// </summary>
        protected virtual UIControl? HitTestRecursive(UIControl control, double localX, double localY)
        {
            if (!control.ContainsLocalPoint(localX, localY))
            {
                return null;
            }

            for (int i = control.Children.Count - 1; i >= 0; i--)
            {
                UIControl child = control.Children[i];
                UIControl? hit = child.HitTestRecursive(child, localX, localY);
                if (hit != null)
                {
                    return hit;
                }
            }

            return control;
        }
        #endregion

        #region Property Change Notification
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        #endregion
    }
}