using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.ControlTypes.Custom;
using IS2Mod.ControlTypes.Events;
using IS2Mod.Enums;
using ModernVintageGUI.Enums;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;

namespace ModernVintageGUI.ControlTypes
{
    /// <summary>
    /// Tells a handler which entry was picked, so a mod can subscribe once on the menu instead of
    /// once per entry.
    /// </summary>
    public class ContextMenuItemEventArgs : EventArgs
    {
        /// <summary>The entry that was picked.</summary>
        public ContextMenuItem Item { get; }

        /// <summary>
        /// The entry and every entry it is nested under, outermost first. For a top level entry
        /// this holds just that entry; picking "Text 2" under "More" gives ["More", "Text 2"].
        /// </summary>
        public IReadOnlyList<ContextMenuItem> Path { get; }

        /// <summary>Shorthand for the caption of <see cref="Item"/>.</summary>
        public string Text => Item.Text;

        public ContextMenuItemEventArgs(ContextMenuItem item, IReadOnlyList<ContextMenuItem> path)
        {
            Item = item;
            Path = path;
        }
    }

    /// <summary>
    /// A context menu that hangs on another control.
    ///
    /// The control itself is a zero sized anchor inside the host tree: it costs no layout space,
    /// but the layout gives it a position, and that position is what the popup is placed at. The
    /// menu itself lives in its own <see cref="CustomDialogElement"/> in the overlay render band,
    /// so it can extend past the host dialog instead of being clipped by its surface.
    ///
    /// Because the anchor is part of the tree, its position is recomputed by every layout pass -
    /// so reopening the menu after the host moved or the GUI scale changed lands in the right
    /// place without any tracking.
    /// </summary>
    public class ContextMenuControl : UIControl, IDisposable
    {
        #region Properties
        /// <summary>The control this menu belongs to. The popup is placed relative to it.</summary>
        public UIControl Owner { get; }

        /// <summary>Which corner of <see cref="Owner"/> the popup is placed at.</summary>
        public ContextMenuAnchor Anchor { get; set; }

        /// <summary>
        /// Shifted from the anchor corner, in device pixels. Lets a menu line up with something
        /// inside its owner rather than with the owner itself - the burger icon of a title bar,
        /// for instance, which is not a control of its own.
        /// </summary>
        public PointD Offset { get; set; }

        public IReadOnlyList<ContextMenuItem> Items { get; }

        public bool IsOpen => _popup != null && _popup.IsVisible;

        /// <summary>
        /// Raised when an entry of this menu is picked - and also when one of a menu nested below
        /// it is, because the event bubbles up the cascade. Subscribing on the menu you opened is
        /// therefore enough to see every pick, no matter how deep it sits.
        ///
        /// The sender is the menu the entry actually belongs to, so a handler can tell the level
        /// apart if it cares; which entry it was is in the arguments.
        /// </summary>
        public event EventHandler<ContextMenuItemEventArgs>? ItemActivated;
        #endregion

        /// <summary>GuiElementListMenu strokes its box with LineWidth 2.</summary>
        private const int MenuBorderWidth = 2;

        #region Private Fields
        private readonly string _title;
        private readonly RectangleControl _itemStack;
        private CustomDialogElement? _popup;
        private bool _isDisposed;
        #endregion

        #region Constructor
        /// <param name="owner">
        /// The control the menu hangs on. The menu adds itself to its children, so the layout
        /// gives the anchor a position.
        /// </param>
        public ContextMenuControl(
            UIControl owner,
            List<ContextMenuItem> items,
            string contextMenuTitle = "ContextMenu",
            ContextMenuAnchor contextMenuAnchor = ContextMenuAnchor.BottomLeft)
            : base(_Orientation: Orientation.None, _Margin: 0, _Padding: 0)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Items = items ?? new List<ContextMenuItem>();
            Anchor = contextMenuAnchor;
            _title = contextMenuTitle;

            // Vertical stack - a menu lists its entries downwards - carrying the menu background.
            _itemStack = CreateMenuBackground(contextMenuTitle + "_items");

            foreach (ContextMenuItem item in Items)
            {
                item.OwnerMenu = this;
                _itemStack.Children.Add(item);
            }

            // The popup itself cannot be built here: it needs the ICoreClientAPI, which is only
            // reachable through Dialog once this anchor is part of a laid out tree. Dialog is
            // always null during construction, so the popup is created on first Show().
            owner.Children.Add(this);
        }
        #endregion

        /// <summary>
        /// The box a menu is drawn on, matching GuiElementListMenu: a solid
        /// GuiStyle.DialogStrongBgColor fill with a 2px stroke in black at half alpha. No emboss,
        /// no blur, no light edge - those belong to the title bar, not to the menu.
        ///
        /// Public and static so the layout harness can render the exact same box.
        /// </summary>
        public static RectangleControl CreateMenuBackground(string name)
        {
            var background = new RectangleControl(
                borderWidth: MenuBorderWidth,
                borderColor: new ElementColor(0.0, 0.0, 0.0, 0.5),
                backgroundColor: new ElementColor(GuiStyle.DialogStrongBgColor),
                _Name: name,
                _Margin: 0,
                _Padding: 0);

            background.InsideOrientation = Orientation.Top;

            return background;
        }

        #region Open / Close
        public void Show()
        {
            if (_isDisposed)
                return;

            CustomDialogElement? popup = EnsurePopup();
            if (popup == null)
                return;

            if (popup.IsVisible)
                return;

            // Lay out first so the popup knows its size, then place it - PositionAtOwner needs
            // the size to keep the menu on screen, and SetPosition survives later layout passes.
            popup.AutoCenter = false;
            popup.Show();
            PositionAtOwner(popup);
        }

        public void Hide()
        {
            if (_popup == null)
                return;

            // Close sub menus first, otherwise they would stay on screen without their parent.
            foreach (ContextMenuItem item in Items)
            {
                item.SubMenu?.Hide();
            }

            _popup.Hide();
        }

        public void Toggle()
        {
            if (IsOpen)
                Hide();
            else
                Show();
        }

        /// <summary>
        /// Raises <see cref="ItemActivated"/> here and then on every menu this one was opened
        /// from, so a single subscription on the outermost menu sees picks from all levels.
        /// </summary>
        internal void RaiseItemActivated(ContextMenuItemEventArgs args)
        {
            ItemActivated?.Invoke(this, args);

            if (Owner is ContextMenuItem ownerItem)
            {
                ownerItem.OwnerMenu?.RaiseItemActivated(args);
            }
        }

        /// <summary>
        /// Closes this menu and every menu it was opened from. Picking an entry in a sub menu
        /// dismisses the whole cascade, not just the level it sits in.
        /// </summary>
        public void HideChain()
        {
            Hide();

            if (Owner is ContextMenuItem ownerItem)
            {
                ownerItem.OwnerMenu?.HideChain();
            }
        }

        private CustomDialogElement? EnsurePopup()
        {
            if (_popup != null)
                return _popup;

            ICoreClientAPI? capi = Dialog?.Api;
            if (capi == null)
            {
                // Not attached to an open dialog yet - nothing sensible to show.
                return null;
            }

            _popup = new CustomDialogElement(capi, _title, _title, DialogRenderLayer.Overlay)
            {
                // The menu box is drawn by the item stack, matching GuiElementListMenu - the
                // dialog background with its dirt texture would be wrong here.
                DrawsBackground = false,

                // Dismissable: UIManager closes it when a mouse button goes down outside of it.
                CloseOnOutsideClick = true,

                AutoCenter = false
            };

            // The dialog constructor forces a padding of 10. Half the menu border stroke sits
            // outside the box, so leave exactly that much room - otherwise it gets clipped away
            // by the edge of the popup surface.
            _popup.Padding = MenuBorderWidth / 2.0;

            _popup.Children.Add(_itemStack);

            return _popup;
        }
        #endregion

        #region Positioning
        /// <summary>
        /// Places the popup at the requested corner of the owner and keeps it on screen.
        /// </summary>
        private void PositionAtOwner(CustomDialogElement popup)
        {
            PointD ownerPos = Owner.GetScreenPosition();
            double ownerWidth = Owner.Size.X;
            double ownerHeight = Owner.Size.Y;

            double x = ownerPos.X;
            double y = ownerPos.Y;

            switch (Anchor)
            {
                case ContextMenuAnchor.TopLeft:
                    break;
                case ContextMenuAnchor.TopCenter:
                    x += ownerWidth / 2;
                    break;
                case ContextMenuAnchor.TopRight:
                    x += ownerWidth;
                    break;
                case ContextMenuAnchor.LeftCenter:
                    y += ownerHeight / 2;
                    break;
                case ContextMenuAnchor.RightCenter:
                    x += ownerWidth;
                    y += ownerHeight / 2;
                    break;
                case ContextMenuAnchor.BottomLeft:
                    y += ownerHeight;
                    break;
                case ContextMenuAnchor.BottomCenter:
                    x += ownerWidth / 2;
                    y += ownerHeight;
                    break;
                case ContextMenuAnchor.BottomRight:
                    x += ownerWidth;
                    y += ownerHeight;
                    break;
            }

            x += Offset.X;
            y += Offset.Y;

            // A menu opened near the right or bottom edge would otherwise hang off screen.
            double frameWidth = popup.Api.Render.FrameWidth;
            double frameHeight = popup.Api.Render.FrameHeight;

            x = Math.Max(0, Math.Min(x, frameWidth - popup.Size.X));
            y = Math.Max(0, Math.Min(y, frameHeight - popup.Size.Y));

            popup.SetPosition(x, y);
        }
        #endregion

        #region Layout
        /// <summary>
        /// The anchor takes no space in the host tree.
        ///
        /// Forcing zero here is the right call and does not break the layout: the measure pass is
        /// free to return whatever a control wants, and a zero size contributes nothing to a
        /// stacking parent (its spacing is 2 x Margin, which is 0 here). It also sidesteps the
        /// constructor quirk that a passed size of 0/0 turns a control into an auto-sizing one.
        ///
        /// One thing to know: the arrange pass afterwards stretches children to the parent
        /// content width, so Size will not stay 0 - only Position is meaningful on an anchor,
        /// which is all it is used for.
        /// </summary>
        public override PointD CalculateSize()
        {
            // Sub menu anchors of our own items still have to be measured.
            foreach (UIControl child in Children)
            {
                child.CalculateSize();
            }

            CalculatedSize = new PointD(0, 0);
            SetLayoutSize(CalculatedSize);

            return CalculatedSize;
        }

        /// <summary>The anchor draws nothing - the popup has its own surface.</summary>
        public override void GenerateRenderData(ImageSurface surface, Context context)
        {
        }
        #endregion

        #region Dispose
        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            foreach (ContextMenuItem item in Items)
            {
                item.SubMenu?.Dispose();
            }

            // The popup registered an IRenderer in its constructor; dropping it without
            // disposing would leak that renderer and its GL texture.
            _popup?.Dispose();
            _popup = null;
        }
        #endregion
    }

    /// <summary>
    /// One entry of a <see cref="ContextMenuControl"/>.
    ///
    /// Deliberately not a <see cref="ButtonControl"/>: vanilla menu entries are flat text rows on
    /// the shared menu background, drawn by GuiElementListMenu - no border, no emboss, no shadow.
    /// A button would bring its embossed frame and look nothing like the original, so an entry is
    /// its own composite of a hover fill plus a label, the same way a button is a composite of a
    /// rectangle plus a label.
    ///
    /// The values below are taken from GuiElementListMenu.ComposeDynamicElements().
    /// </summary>
    public class ContextMenuItem : UIControl
    {
        #region Vanilla styling
        /// <summary>GuiElementListMenu.unscaledLineHeight.</summary>
        private const double RowHeight = 30.0;

        /// <summary>Vanilla indents entry text by 5 * scale from the left.</summary>
        private const int TextPadding = 5;

        /// <summary>Safety cap when walking the cascade upwards.</summary>
        private const int MaxMenuDepth = 32;

        /// <summary>GuiStyle.SmallFontSize, what CairoFont.WhiteSmallText() uses.</summary>
        private const int FontSize = 16;

        /// <summary>
        /// GuiStyle.DialogHighlightColor, but vanilla overwrites its alpha with 0.5 before
        /// filling the hovered row.
        /// </summary>
        private const double HoverAlpha = 0.5;
        #endregion

        #region Properties
        public string Text
        {
            get => _label.Text;
            set => _label.Text = value;
        }

        /// <summary>
        /// The menu this entry belongs to. Named OwnerMenu on purpose - Parent is the layout
        /// parent on UIControl and must not be shadowed, the layout engine relies on it.
        /// </summary>
        public ContextMenuControl? OwnerMenu { get; internal set; }

        public IReadOnlyList<ContextMenuItem> ChildItems { get; }

        /// <summary>The nested menu, present only when this entry has child items.</summary>
        public ContextMenuControl? SubMenu { get; private set; }

        /// <summary>Raised when an entry without child items is clicked.</summary>
        public event EventHandler? Activated;
        #endregion

        private readonly RectangleControl _hoverFill;
        private readonly TextLabelControl _label;

        public ContextMenuItem(string text, List<ContextMenuItem>? childItems = null)
            : base(_Orientation: Orientation.None, _Margin: 0, _Padding: 0)
        {
            ChildItems = childItems ?? new List<ContextMenuItem>();

            // Full row highlight, transparent until the cursor is on the entry.
            _hoverFill = new RectangleControl(
                borderWidth: 0,
                borderColor: ElementColor.Transparent,
                backgroundColor: HoverColor(0.0),
                _Name: text + "_hover",
                _Margin: 0,
                _Padding: 0);

            _label = new TextLabelControl(
                text: text,
                fontName: GuiStyle.StandardFontName,
                fontSize: FontSize,
                textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
                orientation: TextOrientation.MiddleLeft,
                padding: TextPadding,
                _Name: text + "_label",
                _Margin: 0,
                _Padding: 0);

            Children.Add(_hoverFill);
            Children.Add(_label);

            // Entries are what a menu is operated with, so they carry the keyboard focus of the
            // popup: Tab and the arrow keys walk them, Enter picks one, Escape closes the menu.
            IsFocusable = true;

            Enter += OnEnter;
            Exit += OnExit;
            Clicked += OnClicked;
            GotFocus += OnGotFocus;
            LostFocus += OnLostFocus;
        }

        private static ElementColor HoverColor(double alpha)
        {
            var color = new ElementColor(GuiStyle.DialogHighlightColor);
            color.A = (byte)(alpha * 255);
            return color;
        }

        #region Layout
        /// <summary>
        /// Width comes from the text, height is the fixed vanilla row height. Both children are
        /// then stretched over the whole row so the highlight covers it and the label centers in
        /// it - the same approach ButtonControl uses for its own parts.
        /// </summary>
        /// <summary>
        /// An entry is an atomic hit target. Without this the hit test would descend into the
        /// label or the highlight rectangle, and those would receive Enter, Exit and Clicked
        /// instead of the entry - so the entry would never light up and never fire.
        /// </summary>
        protected override UIControl? HitTestRecursive(UIControl control, double localX, double localY)
        {
            return control.ContainsLocalPoint(localX, localY) ? control : null;
        }

        public override PointD CalculateSize()
        {
            // Every child, not just the two parts: an entry with a sub menu also carries that
            // menu's zero sized anchor.
            foreach (UIControl child in Children)
            {
                child.CalculateSize();
            }

            PointD labelSize = _label.Size;

            PointD measured = new PointD(
                labelSize.X,
                Math.Max(labelSize.Y, RowHeight * LayoutScale));

            CalculatedSize = measured;
            SetLayoutSize(measured);

            StretchParts();

            return measured;
        }

        public override void NormalizeChildrenByDelta()
        {
            StretchParts();
            base.NormalizeChildrenByDelta();
        }

        public override void CalculateAllPositions()
        {
            base.CalculateAllPositions();
            StretchParts();
        }

        private void StretchParts()
        {
            _hoverFill.SetLayoutSize(Size);
            _hoverFill.Position = Position;

            _label.SetLayoutSize(Size);
            _label.Position = Position;
        }
        #endregion

        #region Interaction
        /// <summary>
        /// A menu entry has one highlight but two ways of being singled out, and it can be in
        /// both at once - so they are tracked separately and the row is lit when either holds.
        /// Letting Enter and Exit write the colour directly would mean the mouse leaving an
        /// entry unlights the one the keyboard is sitting on.
        /// </summary>
        private bool _isHovered;
        private bool _isFocusHighlighted;

        private void UpdateHighlight()
        {
            _hoverFill.BackgroundColor = HoverColor(_isHovered || _isFocusHighlighted ? HoverAlpha : 0.0);
            Dialog?.Refresh();
        }

        private void OnEnter(object? sender, MouseEventArgs e)
        {
            _isHovered = true;
            UpdateHighlight();

            // Hovering also moves the keyboard selection, the way menus work everywhere. Without
            // it the two could point at different entries, and Enter would pick the one the
            // player is not looking at.
            Dialog?.FocusControl(this);
        }

        private void OnExit(object? sender, MouseEventArgs e)
        {
            _isHovered = false;
            UpdateHighlight();
        }

        private void OnGotFocus(object? sender, EventArgs e)
        {
            _isFocusHighlighted = true;
            UpdateHighlight();
        }

        private void OnLostFocus(object? sender, EventArgs e)
        {
            _isFocusHighlighted = false;
            UpdateHighlight();
        }

        /// <summary>
        /// Builds the nested menu. Deferred until the entry is part of a tree, because a menu
        /// needs a reachable dialog to get the client API from.
        /// </summary>
        private void EnsureSubMenu()
        {
            if (SubMenu != null || ChildItems.Count == 0)
                return;

            // A sub menu opens to the side of its entry, like every cascading menu does.
            SubMenu = new ContextMenuControl(
                this,
                new List<ContextMenuItem>(ChildItems),
                Text + "_submenu",
                ContextMenuAnchor.TopRight);
        }

        private void OnClicked(object? sender, MouseEventArgs e)
        {
            if (ChildItems.Count > 0)
            {
                EnsureSubMenu();
                SubMenu?.Show();
                return;
            }

            Activated?.Invoke(this, EventArgs.Empty);

            // Raised before closing, so a handler can still look at the open menu.
            OwnerMenu?.RaiseItemActivated(new ContextMenuItemEventArgs(this, BuildPath()));

            // A leaf entry closes the whole cascade, the way a menu command does everywhere.
            OwnerMenu?.HideChain();
        }

        /// <summary>
        /// This entry and every entry it is nested under, outermost first.
        /// </summary>
        private IReadOnlyList<ContextMenuItem> BuildPath()
        {
            var path = new List<ContextMenuItem> { this };

            ContextMenuControl? menu = OwnerMenu;

            // Menus form a tree, so this terminates - the cap is only there so a hand built
            // cycle would not hang the client.
            for (int depth = 0; depth < MaxMenuDepth && menu?.Owner is ContextMenuItem parent; depth++)
            {
                path.Insert(0, parent);
                menu = parent.OwnerMenu;
            }

            return path;
        }
        #endregion
    }
}
