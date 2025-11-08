using IS2Mod.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;

namespace IS2Mod.ControlTypes
{
    public class DialogElement : GuiDialog
    {
        public override string ToggleKeyCombinationCode => "";
        public UIControl MainContainer { get; set; }
        public string DialogName { get; set; } = "";
        public string Title { get; set; } = "";

        public DialogElement(ICoreClientAPI capi, UIControl _MainContainer, string _DialogName, string _Title = "") : base(capi)
        {
            MainContainer = _MainContainer;
            DialogName = _DialogName;
            Title = _Title;
            ComposeDialog();

        }

        public void ComposeDialog()
        {
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.CenterMiddle);

            ElementBounds bgBounds = ElementBounds.Fill
                .WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.Fixed;

            var compo = capi.Gui.CreateCompo(DialogName, dialogBounds);

            compo.AddDialogTitleBar(Title, OnTitleBarClose);
            compo.AddDialogBG(bgBounds, true);
            compo.BeginChildElements(bgBounds);  // <-- IMPORTANT
            
            MainContainer.Margin = 15;
            MainContainer.Padding = 15;
            MainContainer.Composer = compo;
            MainContainer.CompileAllObjects(MainContainer.Children);
            bgBounds = bgBounds.WithFixedHeight(MainContainer.Size.Y).WithFixedWidth(MainContainer.Size.X);
            bgBounds.WithFixedPosition(MainContainer.Position.X, MainContainer.Position.Y);
            compo.EndChildElements();

            SingleComposer = compo.Compose();
            
        }
        public void OnMouseDown(MouseEvent args)
        {

            args.Handled = true;
        }

        void MouseDown()
        {

        }

        void OnTitleBarClose() => TryClose();

        bool OnCloseButton()
        {
            TryClose();
            return true;
        }
    }
}
