using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.ControlTypes.Events;
using IS2Mod.Enums;
using IS2Mod.Interfaces;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ModernVintageGUI.ControlTypes
{
    /// <summary>Which node was picked or opened.</summary>
    public class TreeNodeEventArgs : EventArgs
    {
        /// <summary>The node, or null when the selection was cleared.</summary>
        public TreeNode? Node { get; }

        /// <summary>Shorthand for the payload the caller attached to the node.</summary>
        public object? Value => Node?.Value;

        public TreeNodeEventArgs(TreeNode? node)
        {
            Node = node;
        }
    }

    /// <summary>
    /// One node of a <see cref="TreeViewControl"/>, and the branch hanging under it.
    ///
    /// A node is data, not a control. The rows the player sees are made by the tree from the
    /// nodes that are currently visible and thrown away again when a branch is folded, which is
    /// what keeps a tree of ten thousand nodes with three of them open costing three rows - a
    /// control per node would cost ten thousand, laid out and measured on every pass.
    /// </summary>
    public class TreeNode
    {
        #region Properties
        /// <summary>The caption.</summary>
        public string Text { get; set; }

        /// <summary>One of the game's GUI icons, drawn in the icon column.</summary>
        public string? IconName { get; set; }

        /// <summary>An item stack, drawn in the icon column instead of an icon.</summary>
        public ItemStack? Stack { get; set; }

        /// <summary>Whatever the caller wants to get back out of the selection.</summary>
        public object? Value { get; set; }

        /// <summary>The branch under this node, in order.</summary>
        public IReadOnlyList<TreeNode> Children => _children;

        /// <summary>The node this one hangs under, or null for a root.</summary>
        public TreeNode? Parent { get; private set; }

        /// <summary>True when there is a branch to fold out.</summary>
        public bool HasChildren => _children.Count > 0;

        /// <summary>How far this node sits from a root. A root is 0.</summary>
        public int Depth => Parent == null ? 0 : Parent.Depth + 1;

        /// <summary>
        /// Whether the branch under this node is folded out. Setting it rebuilds the rows of the
        /// tree it belongs to, so a mod can open a branch from code exactly as a click does.
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                    return;

                _isExpanded = value;
                Owner?.OnNodeExpandedChanged(this);
            }
        }

        /// <summary>The tree this node belongs to, or null while it is still detached.</summary>
        internal TreeViewControl? Owner
        {
            get => _owner;
            set
            {
                _owner = value;

                foreach (TreeNode child in _children)
                {
                    child.Owner = value;
                }
            }
        }
        #endregion

        private readonly List<TreeNode> _children = new List<TreeNode>();
        private TreeViewControl? _owner;
        private bool _isExpanded;

        public TreeNode(string text, object? value = null, string? iconName = null, ItemStack? stack = null)
        {
            Text = text ?? "";
            Value = value;
            IconName = iconName;
            Stack = stack;
        }

        /// <summary>A node built straight out of the children it is to hold.</summary>
        public TreeNode(string text, IEnumerable<TreeNode> children, object? value = null)
            : this(text, value)
        {
            foreach (TreeNode child in children)
            {
                Add(child);
            }
        }

        #region Branch
        public TreeNode Add(TreeNode child)
        {
            if (child == null)
                throw new ArgumentNullException(nameof(child));

            child.Parent = this;
            child.Owner = Owner;
            _children.Add(child);

            Owner?.Rebuild();

            return child;
        }

        /// <summary>Adds a plain caption node, the everyday case.</summary>
        public TreeNode Add(string text, object? value = null, string? iconName = null)
        {
            return Add(new TreeNode(text, value, iconName));
        }

        public void Remove(TreeNode child)
        {
            if (!_children.Remove(child))
                return;

            child.Parent = null;
            child.Owner = null;

            Owner?.Rebuild();
        }

        public void Clear()
        {
            foreach (TreeNode child in _children)
            {
                child.Parent = null;
                child.Owner = null;
            }

            _children.Clear();

            Owner?.Rebuild();
        }
        #endregion

        #region Walking
        public void Expand()
        {
            IsExpanded = true;
        }

        public void Collapse()
        {
            IsExpanded = false;
        }

        /// <summary>
        /// Folds out this node and everything under it. One rebuild at the end rather than one
        /// per node: the tree is told once, by the caller of this, and not by every setter on
        /// the way down.
        /// </summary>
        public void ExpandAll()
        {
            SetExpandedDeep(true);
            Owner?.Rebuild();
        }

        public void CollapseAll()
        {
            SetExpandedDeep(false);
            Owner?.Rebuild();
        }

        /// <summary>
        /// Folds this node and its whole branch without telling the tree. The caller rebuilds
        /// once when the walk is done - a rebuild per node would relayout the dialog a thousand
        /// times for one ExpandAll.
        /// </summary>
        internal void SetExpandedDeep(bool expanded)
        {
            _isExpanded = expanded;

            foreach (TreeNode child in _children)
            {
                child.SetExpandedDeep(expanded);
            }
        }

        /// <summary>
        /// Folds out everything between this node and the root, so that it is visible. Does not
        /// fold out the node itself - making a node visible and opening its branch are two
        /// different wishes.
        /// </summary>
        public void ExpandToHere()
        {
            for (TreeNode? node = Parent; node != null; node = node.Parent)
            {
                node._isExpanded = true;
            }

            Owner?.Rebuild();
        }

        /// <summary>This node and every node under it, depth first, folded out or not.</summary>
        public IEnumerable<TreeNode> Descendants()
        {
            yield return this;

            foreach (TreeNode child in _children)
            {
                foreach (TreeNode node in child.Descendants())
                {
                    yield return node;
                }
            }
        }
        #endregion
    }

    /// <summary>
    /// A tree of nodes that fold out and back in.
    ///
    /// It scrolls like every other container here - it is an <see cref="IScrollable"/> inherited
    /// whole from <see cref="RectangleControl"/> - and its rows are the same
    /// <see cref="ListRowControl"/> a dropdown and a list view are built from, so a tree, a list
    /// and a menu read as one family.
    ///
    /// What it adds is the fold: the rows are made from the nodes that are visible right now and
    /// rebuilt whenever that changes. Clicking the expander opens or closes a branch, clicking
    /// anywhere else picks the node - and the two are told apart by where in the row the click
    /// landed, because a row is one hit target and gets the whole row's clicks.
    ///
    /// <code>
    /// var tree = new TreeViewControl();
    /// TreeNode rocks = tree.AddNode("Rocks");
    /// rocks.Add("Granite");
    /// rocks.Add("Chalk");
    /// rocks.Expand();
    /// </code>
    /// </summary>
    public class TreeViewControl : RectangleControl, IScrollable
    {
        #region Styling
        /// <summary>What the tree is sized to when the caller says nothing, in author units.</summary>
        public const double UnscaledDefaultWidth = 240.0;
        public const double UnscaledDefaultHeight = 200.0;

        /// <summary>How far one level is indented, in author units.</summary>
        public const double UnscaledIndent = 14.0;

        /// <summary>The column the expander triangle sits in, left of the icon column.</summary>
        public const double UnscaledExpanderWidth = 14.0;

        /// <summary>GuiElementListMenu strokes its box with LineWidth 2.</summary>
        private const int TreeBorderWidth = 2;
        #endregion

        #region Properties
        /// <summary>The roots of the tree, in order.</summary>
        public IReadOnlyList<TreeNode> Nodes => _nodes;

        /// <summary>The picked node, or null when nothing is picked.</summary>
        public TreeNode? SelectedNode
        {
            get => _selected;
            set => Select(value, notify: true);
        }

        /// <summary>The payload the caller attached to the picked node.</summary>
        public object? SelectedValue => _selected?.Value;

        /// <summary>The nodes that are visible right now, top to bottom.</summary>
        public IEnumerable<TreeNode> VisibleNodes => Flatten(_nodes);

        /// <summary>
        /// How the rows are laid out. <see cref="DropdownRowStyle.Auto"/> - the default - gives a
        /// tree that has an item stack anywhere in it the roomy handbook rows.
        /// </summary>
        public DropdownRowStyle RowStyle
        {
            get => _rowStyle;
            set
            {
                if (_rowStyle == value)
                    return;

                _rowStyle = value;
                Rebuild();
            }
        }

        /// <summary>
        /// Shade every other visible row a touch differently. Off by default, unlike a flat
        /// list: a tree already gives the eye an indent to follow, and banding that is worked
        /// out from the visible position - the only position there is - shifts under the player
        /// whenever a branch above opens.
        /// </summary>
        public bool RowStriping
        {
            get => _rowStriping;
            set
            {
                if (_rowStriping == value)
                    return;

                _rowStriping = value;
                Rebuild();
            }
        }

        /// <summary>Raised when the picked node changes, by click, by keyboard or from code.</summary>
        public event EventHandler<TreeNodeEventArgs>? SelectionChanged;

        /// <summary>
        /// Raised when a node is clicked or Entered. Fires for a click on the same node again,
        /// unlike <see cref="SelectionChanged"/>, and not for a click on an expander - opening a
        /// branch is not choosing what is in it.
        /// </summary>
        public event EventHandler<TreeNodeEventArgs>? NodeActivated;

        /// <summary>Raised when a branch is folded out or back in.</summary>
        public event EventHandler<TreeNodeEventArgs>? NodeExpandedChanged;
        #endregion

        #region Private fields
        private readonly List<TreeNode> _nodes = new List<TreeNode>();

        private DropdownRowStyle _rowStyle = DropdownRowStyle.Auto;
        private DropdownRowMetrics _metrics = DropdownRowMetrics.Menu;
        private bool _rowStriping;
        private TreeNode? _selected;

        /// <summary>
        /// True while <see cref="Rebuild"/> is running, so the node setters it touches on the
        /// way cannot ask for a second rebuild from inside the first.
        /// </summary>
        private bool _rebuilding;

        /// <summary>How many <see cref="BeginUpdate"/> calls are still open.</summary>
        private int _updateDepth;

        /// <summary>Whether anything asked for a rebuild while they were.</summary>
        private bool _rebuildPending;
        #endregion

        public TreeViewControl(string _Name = "", double _Margin = 5)
            : base(
                borderWidth: TreeBorderWidth,
                borderColor: new ElementColor(0.0, 0.0, 0.0, 0.5),
                backgroundColor: new ElementColor(GuiStyle.DialogStrongBgColor),
                _Name: _Name,
                _Margin: _Margin,
                _Padding: 0)
        {
            InsideOrientation = Orientation.Top;

            Size = new PointD(UnscaledDefaultWidth, UnscaledDefaultHeight);
            IsAutoSize = false;
            EnableVerticalScrollbar = true;
        }

        #region Nodes
        /// <summary>Adds a root node.</summary>
        public TreeNode AddNode(TreeNode node)
        {
            if (node == null)
                throw new ArgumentNullException(nameof(node));

            node.Owner = this;
            _nodes.Add(node);

            Rebuild();

            return node;
        }

        /// <summary>Adds a plain caption root, the everyday case.</summary>
        public TreeNode AddNode(string text, object? value = null, string? iconName = null)
        {
            return AddNode(new TreeNode(text, value, iconName));
        }

        /// <summary>
        /// Replaces the roots. The selection is kept when the picked node is still somewhere in
        /// the new tree and cleared otherwise.
        /// </summary>
        public void SetNodes(IEnumerable<TreeNode>? nodes)
        {
            TreeNode? previous = _selected;

            foreach (TreeNode node in _nodes)
            {
                node.Owner = null;
            }

            _nodes.Clear();

            if (nodes != null)
            {
                foreach (TreeNode node in nodes)
                {
                    if (node == null)
                        continue;

                    node.Owner = this;
                    _nodes.Add(node);
                }
            }

            _selected = previous != null && Contains(previous) ? previous : null;

            Rebuild();
        }

        public void Clear()
        {
            SetNodes(null);
        }

        /// <summary>Folds out every branch of the tree.</summary>
        public void ExpandAll()
        {
            SetExpandedDeep(_nodes, true);
            Rebuild();
        }

        /// <summary>And folds them all back in.</summary>
        public void CollapseAll()
        {
            SetExpandedDeep(_nodes, false);
            Rebuild();
        }

        private static void SetExpandedDeep(IReadOnlyList<TreeNode> nodes, bool expanded)
        {
            foreach (TreeNode node in nodes)
            {
                // The node's own deep setter, which does not call back into the tree - the
                // caller rebuilds once when the whole walk is done.
                node.SetExpandedDeep(expanded);
            }
        }

        /// <summary>
        /// Gives the keyboard to the row showing this node, if it is visible. Used where a
        /// change moves the focus somewhere the player did not ask for - folding a branch in
        /// takes away the row they were standing on, and the parent is where they now are.
        /// </summary>
        public void FocusNode(TreeNode? node)
        {
            if (node == null)
                return;

            foreach (UIControl child in Children)
            {
                if (child is TreeNodeRowControl row && ReferenceEquals(row.Node, node))
                {
                    Dialog?.FocusControl(row);
                    return;
                }
            }
        }

        private bool Contains(TreeNode node)
        {
            foreach (TreeNode root in _nodes)
            {
                foreach (TreeNode candidate in root.Descendants())
                {
                    if (ReferenceEquals(candidate, node))
                        return true;
                }
            }

            return false;
        }

        /// <summary>The visible nodes, top to bottom: a node, then its branch if it is open.</summary>
        private static IEnumerable<TreeNode> Flatten(IReadOnlyList<TreeNode> nodes)
        {
            foreach (TreeNode node in nodes)
            {
                yield return node;

                if (!node.IsExpanded)
                    continue;

                foreach (TreeNode child in Flatten(node.Children))
                {
                    yield return child;
                }
            }
        }
        #endregion

        #region Selection
        /// <summary>Picks a node; null clears the selection.</summary>
        public void Select(TreeNode? node)
        {
            Select(node, notify: true);
        }

        /// <summary>Picks the first node whose <see cref="TreeNode.Value"/> matches.</summary>
        public bool SelectByValue(object? value)
        {
            foreach (TreeNode root in _nodes)
            {
                foreach (TreeNode node in root.Descendants())
                {
                    if (Equals(node.Value, value))
                    {
                        Select(node);
                        return true;
                    }
                }
            }

            return false;
        }

        private void Select(TreeNode? node, bool notify)
        {
            if (ReferenceEquals(_selected, node))
                return;

            _selected = node;

            foreach (UIControl child in Children)
            {
                if (child is TreeNodeRowControl row)
                {
                    row.SetSelected(ReferenceEquals(row.Node, node));
                }
            }

            Dialog?.Refresh();

            if (notify)
            {
                SelectionChanged?.Invoke(this, new TreeNodeEventArgs(node));
            }
        }
        #endregion

        #region Rows
        /// <summary>
        /// Makes the rows from the nodes that are visible right now.
        ///
        /// Everything that changes what is visible ends here: opening a branch, adding a node,
        /// replacing the roots. Rebuilding wholesale rather than patching the difference is
        /// deliberate - the rows carry their position in the visible list for the banding and
        /// their depth for the indent, so any change in the middle would have to walk everything
        /// below it anyway.
        /// </summary>
        internal void Rebuild()
        {
            if (_rebuilding)
                return;

            if (_updateDepth > 0)
            {
                _rebuildPending = true;
                return;
            }

            _rebuilding = true;

            try
            {
                // What had the keyboard, so it can be given back to the same node afterwards -
                // the row holding it is about to be dropped, and a tree that loses the focus to
                // the top of the dialog every time a branch opens cannot be used from the
                // keyboard at all.
                TreeNode? focused = (Dialog?.FocusedControl as TreeNodeRowControl)?.Node;

                Children.Clear();

                var rows = new List<TreeNodeRowControl>();

                foreach (TreeNode node in Flatten(_nodes))
                {
                    var row = new TreeNodeRowControl(node, this);
                    row.SetSelected(ReferenceEquals(node, _selected));

                    rows.Add(row);
                    Children.Add(row);
                }

                _metrics = ListRowControl.ResolveMetrics(_rowStyle, rows);

                ListRowControl.AlignIconColumns(rows, _metrics);
                ListRowControl.NumberRows(rows, _rowStriping);
                ListRowControl.ApplyMetrics(rows, _metrics);

                if (focused != null)
                {
                    foreach (TreeNodeRowControl row in rows)
                    {
                        if (ReferenceEquals(row.Node, focused))
                        {
                            Dialog?.FocusControl(row);
                            break;
                        }
                    }
                }
            }
            finally
            {
                _rebuilding = false;
            }

            RecomposeToMain();
        }

        /// <summary>
        /// Holds the rows still while a batch of nodes is added, and rebuilds once at the
        /// matching <see cref="EndUpdate"/>.
        ///
        /// Every change to the nodes rebuilds the rows and relays out the dialog, which is the
        /// right answer for one change and the wrong one for five hundred. Nesting is allowed -
        /// only the outermost pair rebuilds - so a helper that fills a branch can use it without
        /// knowing whether its caller already did.
        ///
        /// <code>
        /// tree.BeginUpdate();
        /// try { foreach (var thing in things) branch.Add(thing.Name, thing); }
        /// finally { tree.EndUpdate(); }
        /// </code>
        /// </summary>
        public void BeginUpdate()
        {
            _updateDepth++;
        }

        /// <summary>Ends a <see cref="BeginUpdate"/>, rebuilding if anything asked for it.</summary>
        public void EndUpdate()
        {
            if (_updateDepth == 0)
                return;

            _updateDepth--;

            if (_updateDepth > 0 || !_rebuildPending)
                return;

            _rebuildPending = false;
            Rebuild();
        }

        /// <summary>A node was folded out or in.</summary>
        internal void OnNodeExpandedChanged(TreeNode node)
        {
            Rebuild();
            NodeExpandedChanged?.Invoke(this, new TreeNodeEventArgs(node));
        }

        /// <summary>A row was clicked somewhere other than on its expander.</summary>
        internal void OnRowActivated(TreeNode node)
        {
            Select(node, notify: true);
            NodeActivated?.Invoke(this, new TreeNodeEventArgs(node));
        }
        #endregion
    }

    /// <summary>
    /// One visible node, as a row.
    ///
    /// Made by the tree and thrown away when the fold changes, so it holds no state of its own
    /// beyond which node it is showing - everything a player changes lives on the node.
    /// </summary>
    public class TreeNodeRowControl : ListRowControl
    {
        /// <summary>The node this row is showing.</summary>
        public TreeNode Node { get; }

        private readonly TreeViewControl _tree;

        internal TreeNodeRowControl(TreeNode node, TreeViewControl tree)
            : base(node.Text, node.IconName, node.Stack)
        {
            Node = node;
            _tree = tree;

            Name = node.Text;

            KeyDown += OnRowKeyDown;
        }

        #region Layout
        /// <summary>
        /// The indent of this row's level plus the expander column, in author units.
        ///
        /// Every row reserves the expander column whether it has a branch or not, so the
        /// captions of a level line up instead of the childless ones sitting a triangle's width
        /// to the left of their siblings.
        /// </summary>
        private double Indent =>
            Node.Depth * TreeViewControl.UnscaledIndent + TreeViewControl.UnscaledExpanderWidth;

        /// <inheritdoc/>
        protected override double TextLeft => base.TextLeft + Indent;

        /// <summary>Where this row's expander sits, in device pixels, relative to the row.</summary>
        private LayoutRect ExpanderBox()
        {
            double left = Node.Depth * TreeViewControl.UnscaledIndent * LayoutScale;
            double width = TreeViewControl.UnscaledExpanderWidth * LayoutScale;

            return new LayoutRect(left, 0, width, Size.Y);
        }
        #endregion

        #region Rendering
        /// <summary>
        /// The triangle: pointing right at a branch that is folded in, down at one that is
        /// folded out, and nothing at all for a node with no branch - an empty triangle would
        /// promise something to click that is not there.
        /// </summary>
        protected override void DrawRowContent(ImageSurface surface, Context ctx)
        {
            if (!Node.HasChildren)
                return;

            LayoutRect box = ExpanderBox();

            double size = Math.Min(box.Width, Size.Y) * 0.45;
            double centreX = Position.X + box.X + box.Width / 2.0;
            double centreY = Position.Y + Size.Y / 2.0;

            ctx.Save();
            ctx.NewPath();

            if (Node.IsExpanded)
            {
                ctx.MoveTo(centreX - size, centreY - size / 2.0);
                ctx.LineTo(centreX + size, centreY - size / 2.0);
                ctx.LineTo(centreX, centreY + size);
            }
            else
            {
                ctx.MoveTo(centreX - size / 2.0, centreY - size);
                ctx.LineTo(centreX + size, centreY);
                ctx.LineTo(centreX - size / 2.0, centreY + size);
            }

            ctx.ClosePath();

            ctx.SetSourceRGBA(1.0, 1.0, 1.0, Node.IsExpanded ? 0.75 : 0.55);
            ctx.Fill();
            ctx.Restore();
        }
        #endregion

        #region Interaction
        /// <summary>
        /// A click on the expander folds the branch, a click anywhere else picks the node.
        ///
        /// The row is one hit target - it has to be, or the caption would take the clicks - so
        /// the two are told apart here, by where in the row the click landed. Enter on the
        /// focused row arrives here too, reporting the middle of the row, which is never inside
        /// the expander column: it picks the node, and the keyboard folds with Left and Right.
        /// </summary>
        protected override void OnActivated(MouseEventArgs e)
        {
            PointD local = ToLocal(e);

            if (Node.HasChildren && ExpanderBox().Contains(local.X, local.Y))
            {
                Node.IsExpanded = !Node.IsExpanded;
                return;
            }

            _tree.OnRowActivated(Node);
        }

        /// <summary>
        /// Right folds a branch out, Left folds it back in - and Left on a node that is already
        /// closed, or has nothing to close, moves to its parent, which is how every tree in
        /// every file manager behaves.
        ///
        /// Up and Down are deliberately left alone: the dialog walks the focusable controls with
        /// them, and in a tree that is exactly the visible rows in exactly the right order.
        /// </summary>
        private void OnRowKeyDown(object? sender, IS2Mod.ControlTypes.Events.KeyEventArgs e)
        {
            if (e.Handled)
                return;

            if (e.Key == GlKeys.Right)
            {
                if (!Node.HasChildren || Node.IsExpanded)
                    return;

                Node.IsExpanded = true;
                e.Handled = true;
                return;
            }

            if (e.Key != GlKeys.Left)
                return;

            if (Node.HasChildren && Node.IsExpanded)
            {
                Node.IsExpanded = false;
                e.Handled = true;
                return;
            }

            if (Node.Parent == null)
                return;

            // Folding the parent in takes this row off the tree, so the focus has to be handed
            // to the parent's row by hand - it is where the player now is.
            TreeNode parent = Node.Parent;

            parent.IsExpanded = false;
            _tree.Select(parent);
            _tree.FocusNode(parent);

            e.Handled = true;
        }
        #endregion
    }
}
