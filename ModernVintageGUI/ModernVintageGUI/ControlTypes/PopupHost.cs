using IS2Mod.ControlTypes.Custom;
using IS2Mod.Enums;
using ModernVintageGUI.Enums;
using System;
using Cairo;
using Vintagestory.API.Client;

namespace IS2Mod.ControlTypes
{
    /// <summary>
    /// A panel that opens next to a control and closes when the player clicks elsewhere: the
    /// list of a dropdown, the type picker of a selector, a menu.
    ///
    /// It is a <see cref="CustomDialogElement"/> of its own in the overlay render band rather
    /// than a child of the host dialog, and that is the whole point. A panel drawn inside its
    /// host is clipped by the host's surface, so a list opening at the bottom edge of a dialog
    /// is cut off exactly where it needs the room. A dialog of its own is clipped by nothing.
    ///
    /// The host control keeps its position through every layout pass, so a panel reopened after
    /// the dialog moved or the GUI scale changed lands in the right place with no tracking.
    /// </summary>
    public sealed class PopupHost : IDisposable
    {
        private readonly UIControl _owner;
        private readonly UIControl _content;
        private readonly string _name;
        private readonly double _padding;

        private CustomDialogElement? _popup;
        private bool _isDisposed;

        /// <param name="owner">The control the panel opens next to.</param>
        /// <param name="content">
        /// What goes inside. Kept by the caller, so it can be filled and resized while the panel
        /// is closed and simply shown again.
        /// </param>
        /// <param name="padding">
        /// Room to leave around the content, in author units. A frame drawn with a stroke has
        /// half of it outside its own box, and without room for that half the popup surface
        /// clips it away.
        /// </param>
        public PopupHost(UIControl owner, UIControl content, string name, double padding = 0)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _name = string.IsNullOrEmpty(name) ? "popup" : name;
            _padding = padding;
        }

        /// <summary>The dialog behind the panel, once it has been opened at least once.</summary>
        public CustomDialogElement? Dialog => _popup;

        public bool IsOpen => _popup != null && _popup.IsVisible;

        /// <summary>Raised after the panel has been shown and placed.</summary>
        public event EventHandler? Opened;

        /// <summary>
        /// Shows the panel under the owner, or above it when there is no room below - which is
        /// what makes a picker at the bottom of a dialog usable at all.
        /// </summary>
        /// <returns>
        /// false when there is nothing to open into yet: the panel needs the client API, and
        /// that is only reachable once the owner is part of a shown dialog.
        /// </returns>
        public bool Open()
        {
            if (_isDisposed)
                return false;

            CustomDialogElement? popup = EnsurePopup();

            if (popup == null)
                return false;

            if (popup.IsVisible)
                return true;

            // Laid out first, then placed: the placement needs the size to keep the panel on
            // screen, and a position set afterwards survives the later layout passes.
            popup.AutoCenter = false;
            popup.Show();
            Position(popup);

            Opened?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void Close()
        {
            _popup?.Hide();
        }

        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        private CustomDialogElement? EnsurePopup()
        {
            if (_popup != null)
                return _popup;

            ICoreClientAPI? capi = _owner.Dialog?.Api;

            if (capi == null)
                return null;

            _popup = new CustomDialogElement(capi, _name + "_popup", _name, DialogRenderLayer.Overlay)
            {
                // The content draws its own frame; the dialog's dirt background behind it would
                // read as a second window.
                DrawsBackground = false,

                // Dismissable: the UIManager closes it when a mouse button goes down outside.
                CloseOnOutsideClick = true,

                AutoCenter = false
            };

            // The dialog constructor forces a padding of 10, which is a dialog's padding and not
            // a panel's.
            _popup.Padding = _padding;

            _popup.Children.Add(_content);

            return _popup;
        }

        private void Position(CustomDialogElement popup)
        {
            PointD anchor = _owner.GetScreenPosition();

            double x = anchor.X;
            double y = anchor.Y + _owner.Size.Y;

            double frameWidth = popup.Api.Render.FrameWidth;
            double frameHeight = popup.Api.Render.FrameHeight;

            // Below by default, above when that would run off the bottom and there is room up
            // there. Clamped either way, so a panel taller than the screen still starts at the
            // top edge instead of somewhere nobody can reach.
            if (y + popup.Size.Y > frameHeight && anchor.Y - popup.Size.Y >= 0)
            {
                y = anchor.Y - popup.Size.Y;
            }

            x = Math.Max(0, Math.Min(x, frameWidth - popup.Size.X));
            y = Math.Max(0, Math.Min(y, frameHeight - popup.Size.Y));

            popup.SetPosition(x, y);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            // The popup registered an IRenderer in its constructor; dropping it without
            // disposing would leak that renderer and its GL texture.
            _popup?.Dispose();
            _popup = null;
        }
    }
}
