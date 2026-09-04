using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.ControlTypes.Events;
using IS2Mod.Enums;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;

namespace ModernVintageGUI.ControlTypes
{
    /// <summary>Which tab was picked.</summary>
    public class TabSelectedEventArgs : EventArgs
    {
        public TabPage Page { get; }

        public int Index { get; }

        public TabSelectedEventArgs(TabPage page, int index)
        {
            Page = page;
            Index = index;
        }
    }

    /// <summary>
    /// A row of tabs with one page showing at a time.
    ///
    /// The pages belong to the control rather than to the caller, which is the part that makes
    /// it worth having: adding a tab and adding what is on it is one call, and switching tabs is
    /// then nobody's job. A caller who wants to arrange the pages elsewhere can still do that -
    /// leave the content null and listen on <see cref="SelectionChanged"/>.
    ///
    /// <code>
    /// var tabs = new TabsControl();
    /// tabs.AddTab("Input", inputPanel);
    /// tabs.AddTab("Output", outputPanel);
    /// </code>
    /// </summary>
    public class TabsControl : UIControl
    {
        #region Styling
        /// <summary>GuiElementHorizontalTabs draws its tabs 30 units high.</summary>
        private const double UnscaledTabHeight = 30.0;

        /// <summary>Room left and right of a caption inside its tab.</summary>
        private const double UnscaledTabPadding = 12.0;

        /// <summary>The gap between two tabs.</summary>
        private const double UnscaledTabGap = 2.0;

        private const int FontSize = 16;

        /// <summary>The strip a tab is drawn on, and the page below it.</summary>
        private static readonly double[] ActiveTab = { 0.0, 0.0, 0.0, 0.35 };
        private static readonly double[] InactiveTab = { 0.0, 0.0, 0.0, 0.15 };
        #endregion

        #region Properties
        /// <summary>The tabs, in order.</summary>
        public IReadOnlyList<TabPage> Tabs => _tabs;

        /// <summary>Which tab is showing, or -1 when there are none.</summary>
        public int SelectedIndex
        {
            get => _selectedIndex;
            set => Select(value, notify: true);
        }

        public TabPage? SelectedTab =>
            _selectedIndex >= 0 && _selectedIndex < _tabs.Count ? _tabs[_selectedIndex] : null;

        /// <summary>Raised when the showing tab changes, by click, by keyboard or from code.</summary>
        public event EventHandler<TabSelectedEventArgs>? SelectionChanged;
        #endregion

        private readonly List<TabPage> _tabs = new List<TabPage>();
        private readonly RectangleControl _tabStrip;
        private readonly RectangleControl _pageHost;
        private int _selectedIndex = -1;

        public TabsControl(string _Name = "", double _Margin = 5)
            : base(_Name, _Size: null, Orientation.Top, _Margin, _Padding: 0)
        {
            InsideOrientation = Orientation.Top;

            _tabStrip = new RectangleControl(_Name: _Name + "_strip")
            {
                InsideOrientation = Orientation.Left
            };

            _pageHost = new RectangleControl(_Name: _Name + "_pages")
            {
                InsideOrientation = Orientation.Top
            };

            Children.Add(_tabStrip);
            Children.Add(_pageHost);
        }

        #region Tabs
        /// <summary>
        /// Adds a tab. <paramref name="content"/> is shown while that tab is picked and taken
        /// off the tree while it is not - hidden by removal rather than by a flag, so an unseen
        /// page costs no layout and cannot be tabbed into with the keyboard.
        /// </summary>
        public TabPage AddTab(string caption, UIControl? content = null)
        {
            var page = new TabPage(caption, content, this);

            _tabs.Add(page);
            _tabStrip.Children.Add(page.Header);

            if (_selectedIndex < 0)
            {
                Select(_tabs.Count - 1, notify: false);
            }
            else
            {
                RecomposeToMain();
            }

            return page;
        }

        public void Select(int index)
        {
            Select(index, notify: true);
        }

        private void Select(int index, bool notify)
        {
            if (index < -1 || index >= _tabs.Count)
                index = -1;

            if (index == _selectedIndex && _pageHost.Children.Count > 0)
                return;

            _selectedIndex = index;

            _pageHost.Children.Clear();

            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].Header.SetActive(i == index);
            }

            UIControl? content = SelectedTab?.Content;

            if (content != null)
            {
                _pageHost.Children.Add(content);
            }

            RecomposeToMain();

            if (notify && SelectedTab != null)
            {
                SelectionChanged?.Invoke(this, new TabSelectedEventArgs(SelectedTab, index));
            }
        }

        internal void OnHeaderClicked(TabHeader header)
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (ReferenceEquals(_tabs[i].Header, header))
                {
                    Select(i, notify: true);
                    return;
                }
            }
        }
        #endregion

        #region Styling helpers
        internal static ElementColor TabColor(bool active)
        {
            return new ElementColor(active ? ActiveTab : InactiveTab);
        }

        internal static double TabHeight => UnscaledTabHeight;
        internal static double TabPadding => UnscaledTabPadding;
        internal static double TabGap => UnscaledTabGap;
        internal static int TabFontSize => FontSize;
        #endregion
    }

    /// <summary>One tab: its caption, and what is shown while it is picked.</summary>
    public class TabPage
    {
        public string Caption => Header.Text;

        /// <summary>What the tab shows. Null for a tab the caller arranges itself.</summary>
        public UIControl? Content { get; set; }

        internal TabHeader Header { get; }

        internal TabPage(string caption, UIControl? content, TabsControl owner)
        {
            Content = content;
            Header = new TabHeader(caption, owner);
        }
    }

    /// <summary>
    /// The clickable caption of a tab.
    ///
    /// Its own control rather than a ButtonControl, for the same reason a menu entry is: a tab
    /// is a flat strip that is either lit or not, and a button would bring its embossed frame
    /// and look nothing like one.
    /// </summary>
    internal class TabHeader : UIControl
    {
        private readonly RectangleControl _background;
        private readonly TextLabelControl _label;
        private readonly TabsControl _owner;

        private bool _isActive;
        private bool _isHovered;

        public string Text => _label.Text;

        public TabHeader(string caption, TabsControl owner)
            : base(_Name: caption + "_tab", _Size: null, Orientation.None, _Margin: 0, _Padding: 0)
        {
            _owner = owner;

            _background = new RectangleControl(
                borderWidth: 0,
                borderColor: ElementColor.Transparent,
                backgroundColor: TabsControl.TabColor(false),
                _Name: caption + "_tabbg",
                _Margin: 0,
                _Padding: 0);

            _label = new TextLabelControl(
                text: caption,
                fontName: GuiStyle.StandardFontName,
                fontSize: TabsControl.TabFontSize,
                textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
                orientation: TextOrientation.MiddleCenter,
                _Name: caption + "_tablabel",
                _Margin: 0,
                _Padding: 0);

            Children.Add(_background);
            Children.Add(_label);

            IsFocusable = true;

            Clicked += (sender, e) => _owner.OnHeaderClicked(this);
            Enter += (sender, e) => { _isHovered = true; UpdateLook(); };
            Exit += (sender, e) => { _isHovered = false; UpdateLook(); };
            GotFocus += (sender, e) => UpdateLook();
            LostFocus += (sender, e) => UpdateLook();
        }

        public void SetActive(bool active)
        {
            if (_isActive == active)
                return;

            _isActive = active;
            UpdateLook();
        }

        private void UpdateLook()
        {
            _background.BackgroundColor = TabsControl.TabColor(_isActive || _isHovered);
            Dialog?.Refresh();
        }

        /// <summary>A tab is one hit target; its label must not take the click.</summary>
        protected override UIControl? HitTestRecursive(UIControl control, double localX, double localY)
        {
            return control.ContainsLocalPoint(localX, localY) ? control : null;
        }

        public override PointD CalculateSize()
        {
            foreach (UIControl child in Children)
            {
                child.CalculateSize();
            }

            PointD measured = new PointD(
                _label.Size.X + TabsControl.TabPadding * 2 * LayoutScale,
                Math.Max(_label.Size.Y, TabsControl.TabHeight * LayoutScale));

            CalculatedSize = measured;
            SetLayoutSize(measured);

            // Half the gap on each side, so the gap between two tabs is the whole of it.
            Margin = TabsControl.TabGap / 2.0;

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
            _background.SetLayoutSize(Size);
            _background.Position = Position;

            _label.SetLayoutSize(Size);
            _label.Position = Position;
        }

        public override void GenerateRenderData(ImageSurface surface, Context ctx)
        {
            base.GenerateRenderData(surface, ctx);

            if (!_isActive)
                return;

            // A line under the picked tab, the way tab strips everywhere say which one it is.
            ctx.Save();
            ctx.SetSourceRGBA(GuiStyle.DialogHighlightColor);
            ctx.Rectangle(Position.X, Position.Y + Size.Y - 2 * LayoutScale, Size.X, 2 * LayoutScale);
            ctx.Fill();
            ctx.Restore();
        }
    }
}
