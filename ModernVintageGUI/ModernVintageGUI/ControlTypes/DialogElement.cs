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
            bgBounds.BothSizing = ElementSizing.FitToChildren;

            var compo = capi.Gui.CreateCompo(DialogName, dialogBounds);

            compo.AddDialogTitleBar(Title, OnTitleBarClose);
            compo.AddDialogBG(bgBounds, true);
            compo.BeginChildElements(bgBounds);  // <-- IMPORTANT


            MainContainer.Margin = 15;
            MainContainer.Padding = 15;
            MainContainer.Composer = compo;
            MainContainer.CompileAllObjects(MainContainer.Children);

            compo.EndChildElements();

            SingleComposer = compo.Compose();
            
        }

        void OnTitleBarClose() => TryClose();

        bool OnCloseButton()
        {
            TryClose();
            return true;
        }
    }
}
