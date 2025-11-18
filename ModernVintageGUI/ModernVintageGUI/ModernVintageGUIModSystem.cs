using IS2Mod.ControlTypes;
using IS2Mod.ControlTypes.Custom;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace ModernVintageGUI
{
    public class ModernVintageGUIModSystem : ModSystem
    {

        private ICoreClientAPI clientApi;

        // Called on server and client
        // Useful for registering block/entity classes on both sides
        public override void Start(ICoreAPI api)
        {
            Mod.Logger.Notification("Hello from template mod: " + api.Side);


        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            Mod.Logger.Notification("Hello from template mod server side: " + Lang.Get("is2mod:hello"));
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            this.clientApi = api;

            // Registriere das Keyboard Event
            api.Input.RegisterHotKey("openmydialog", "Open My Test Dialog", GlKeys.LControl, HotkeyType.GUIOrOtherControls);
            api.Input.SetHotKeyHandler("openmydialog", OnDialogHotkey);
        }
        CustomDialogElement dialog;
        GuiElementTextButton button;
        //GuiDialog
        private bool OnDialogHotkey(KeyCombination keyCombination)
        {
            if (dialog != null)
            {
                dialog.Dispose();
            }
            // Create a dialog
            dialog = new CustomDialogElement(clientApi, "myDialog", "My Title");

            // Add a button
            var button = new ButtonControl(_Name: "saveButton");
            button.Text = "Save";
            dialog.Children.Add(button);

            var button2 = new ButtonControl(_Name: "saveButton2");
            button2.Text = "Save";
            button2.Size = new Cairo.PointD(150, 150);
            button2.IsAutoSize = false;
            dialog.Children.Add(button2);

            RectangleControl rect = new RectangleControl();
            rect.InsideOrientation = IS2Mod.Enums.Orientation.Left;

            var button3 = new ButtonControl(_Name: "saveButton");
            button3.Text = "Test";
            rect.Children.Add(button3);

            var button23 = new ButtonControl(_Name: "saveButton2");
            button23.Text = "Test";
            rect.Children.Add(button23);

            var txt = new TextLabelControl("Test", _Name: "saveButton2");
            txt.Orientation = TextOrientation.Center;
            rect.Children.Add(txt);

            var button234 = new ButtonControl(_Name: "saveButton2");
            button234.Text = "Test";
            rect.Children.Add(button234);

            var button2345 = new ButtonControl(_Name: "saveButton2");
            button2345.Text = "Test";
            rect.Children.Add(button2345);

            //RectangleControl rect2 = new RectangleControl();
            //rect.InsideOrientation = Enums.Orientation.Right;
            //rect2.Children.Add(button2345);



            dialog.Children.Add(rect);
            //dialog.Children.Add(rect2);
            // Show the dialog
            dialog.Show();

            return true; // true = Event wurde behandelt        }

        }
    }
}
