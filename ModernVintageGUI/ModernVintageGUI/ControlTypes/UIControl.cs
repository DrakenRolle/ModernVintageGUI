using Cairo;
using IS2Mod.ControlTypes.Custom;
using IS2Mod.ControlTypes.Events;
using IS2Mod.Enums;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Vintagestory.API.Client;

namespace IS2Mod.ControlTypes
{
    public abstract class UIControl : INotifyPropertyChanged
    {

        public event EventHandler<MouseEventArgs> Clicked;
        public event EventHandler<MouseEventArgs> Enter;
        public event EventHandler<MouseEventArgs> Exit;
        public event EventHandler<MouseEventArgs> MouseDown;
        public event EventHandler<MouseEventArgs> MouseUp;



        #region Events
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler OnAfterUIControllerBuilt;
        #endregion

        #region Properties
        private LoadedTexture _staticElementsTexture;
        public LoadedTexture StaticElementsTexture
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

        private UIControl _parent;
        public UIControl Parent
        {
            get => _parent;
            set => SetProperty(ref _parent, value);
        }

        private CustomDialogElement _dialog;
        public CustomDialogElement Dialog
        {
            get
            {
                if (_parent != null)
                    return _parent.Dialog;

                if (_dialog != null)
                    return _dialog;

                throw new InvalidOperationException("Dialog not set and no Parent found to get Dialog from.");
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
        public PointD Size
        {
            get => _size;
            set => _size = value;
        }

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

            if (_Size.HasValue)
            {
                _size = _Size.Value;
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
        public virtual void GenerateRenderData(ImageSurface surface, Context context)
        {
            GuiElement.GenerateTexture(Dialog.Api, surface, ref Dialog.StaticElementsTexture.TextureId);

            foreach (var child in Children)
            {
                child.GenerateRenderData(surface, context);
            }
        }
        #endregion

        #region Hierarchy Management
        public void CalculateChildrenRelationship()
        {
            foreach (UIControl child in Children)
            {
                child._parent = this;
                child._dialog = this.Dialog;

                if (child.Children.Count > 0)
                {
                    child.CalculateChildrenRelationship();
                }
            }

            Children.CollectionChanged += Children_CollectionChanged;
        }

        private void Children_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
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

            Dialog?.CalculateSize();
            RecomposeToMain();
        }

        public void RecomposeToMain()
        {
            Dialog?.Refresh();
        }
        #endregion

        #region Size Calculation
        /// <summary>
        /// Calculates the size of this control based on its children and settings.
        /// Does NOT apply clipping - that happens in position calculation.
        /// </summary>
        public virtual PointD CalculateSize()
        {
            // If not auto-size and no children, use the fixed size
            if (!IsAutoSize && Children.Count == 0)
            {
                _calculatedSize = new PointD(Size.X, Size.Y);
                return _calculatedSize;
            }

            PointD calculatedSize = IsAutoSize ? new PointD(0, 0) : new PointD(Size.X, Size.Y);

            // First pass: Calculate initial sizes for all children
            foreach (UIControl child in Children)
            {
                PointD childSize = child.CalculateSize();
                PointD childSizeWithSpacing = GetChildSizeWithSpacing(child, childSize);
                calculatedSize = MergeSizeByOrientation(childSizeWithSpacing, calculatedSize);
            }

            // Store calculated size before any constraints
            _calculatedSize = calculatedSize;

            // Update size if auto-sizing
            if (IsAutoSize)
            {
                Size = calculatedSize;
            }

            // Second pass: Normalize children based on parent's available space
            // This must happen AFTER the parent knows its own size
            NormalizeChildrenByDelta();

            return calculatedSize;
        }

        /// <summary>
        /// Normalizes children sizes based on delta division of parent's available space.
        /// - Top/Bottom orientation: All children get parent's content width
        /// - Left/Right orientation: All children get parent's content height
        /// - None orientation: No normalization
        /// This is applied recursively to all descendants.
        /// </summary>
        public void NormalizeChildrenByDelta()
        {
            if (Children.Count == 0)
                return;

            // Calculate available content area (parent size minus padding)
            double availableWidth = Size.X - (Padding * 2);
            double availableHeight = Size.Y - (Padding * 2);

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
                double childAvailableWidth = availableWidth - (child.Margin * 2);

                // Ensure we don't set negative or zero width
                childAvailableWidth = Math.Max(1, childAvailableWidth);

                // Update child size while preserving height
                child.Size = new PointD(childAvailableWidth, child.Size.Y);
                child._calculatedSize = new PointD(childAvailableWidth, child._calculatedSize.Y);
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
                double childAvailableHeight = availableHeight - (child.Margin * 2);

                // Ensure we don't set negative or zero height
                childAvailableHeight = Math.Max(1, childAvailableHeight);

                // Update child size while preserving width
                child.Size = new PointD(child.Size.X, childAvailableHeight);
                child._calculatedSize = new PointD(child._calculatedSize.X, childAvailableHeight);
            }
        }

        /// <summary>
        /// Adds margin and padding to a child's size for layout calculations.
        /// </summary>
        private PointD GetChildSizeWithSpacing(UIControl child, PointD childSize)
        {
            double totalMargin = 2 * child.Margin;
            double totalPadding = 2 * this.Padding;

            return new PointD(
                childSize.X + totalMargin + totalPadding,
                childSize.Y + totalMargin + totalPadding
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
                UIControl previousSibling = i > 0 ? Children[i - 1] : null;
                Children[i].CalculatePosition(previousSibling);
                Children[i].CalculateAllPositions();
            }
        }

        /// <summary>
        /// Calculates the position of this control relative to its parent and siblings.
        /// Also applies clipping if the control extends beyond parent bounds.
        /// </summary>
        private void CalculatePosition(UIControl previousSibling)
        {
            if (Parent == null)
            {
                Position = new PointD(0, 0);
                return;
            }

            // Calculate base position with parent padding and own margin
            double posX = Parent.Position.X + Parent.Padding + Margin;
            double posY = Parent.Position.Y + Parent.Padding + Margin;

            // Adjust position based on previous sibling and parent orientation
            if (previousSibling != null)
            {
                switch (Parent.InsideOrientation)
                {
                    case Orientation.Top:
                    case Orientation.Bottom:
                        // Stack vertically - keep X, add to Y
                        posY = previousSibling.Position.Y + previousSibling.Size.Y + previousSibling.Margin + Margin;
                        break;

                    case Orientation.Left:
                    case Orientation.Right:
                        // Stack horizontally - add to X, keep Y
                        posX = previousSibling.Position.X + previousSibling.Size.X + previousSibling.Margin + Margin;
                        break;

                    case Orientation.None:
                        // Overlay - use parent position (already set above)
                        break;
                }
            }

            // Apply clipping if control extends beyond parent bounds
            PointD clippedSize = CalculateClippedSize(posX, posY);

            Position = new PointD(posX, posY);
            Size = clippedSize;
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
            double parentMinX = Parent.Position.X + Parent.Padding;
            double parentMinY = Parent.Position.Y + Parent.Padding;
            double parentMaxX = Parent.Position.X + Parent.Size.X - Parent.Padding;
            double parentMaxY = Parent.Position.Y + Parent.Size.Y - Parent.Padding;

            // Start with calculated size
            double clippedWidth = _calculatedSize.X;
            double clippedHeight = _calculatedSize.Y;

            // Clip right edge
            if (proposedX + _calculatedSize.X > parentMaxX)
            {
                clippedWidth = Math.Max(0, parentMaxX - proposedX);
            }

            // Clip bottom edge
            if (proposedY + _calculatedSize.Y > parentMaxY)
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

        #region Property Change Notification
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
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