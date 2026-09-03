using HarmonyLib;
using IS2Mod.ControlTypes;
using IS2Mod.ControlTypes.Custom;
using IS2Mod.Input;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace ModernVintageGUI
{
    public class ModernVintageGUIModSystem : ModSystem
    {
        public const string HarmonyId = "modernvintagegui";
        private const string TestDialogHotkey = "mvgui_testdialog";

        private ICoreClientAPI? clientApi;
        private Harmony? harmony;
        private UIManager? uiManager;
        private CustomDialogElement? dialog;

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

            // Patches ClientMain.UpdateFreeMouse so the cursor stays free while a custom dialog
            // is open. Without it the game re-grabs the mouse on the next rendered frame.
            harmony = new Harmony(HarmonyId);
            harmony.PatchAll(typeof(ModernVintageGUIModSystem).Assembly);

            // Routes mouse input from the client event API into the open dialogs
            uiManager = new UIManager(api);

            api.Input.RegisterHotKey(TestDialogHotkey, "Toggle ModernVintageGUI test dialog", GlKeys.J, HotkeyType.GUIOrOtherControls);
            api.Input.SetHotKeyHandler(TestDialogHotkey, OnDialogHotkey);
        }

        private bool OnDialogHotkey(KeyCombination keyCombination)
        {
            if (clientApi == null)
            {
                return false;
            }

            if (dialog == null)
            {
                dialog = BuildTestDialog(clientApi);
            }

            dialog.Toggle();

            return true; // true = event was handled
        }

        private static CustomDialogElement BuildTestDialog(ICoreClientAPI capi)
        {
            var testDialog = new CustomDialogElement(capi, "myDialog", "My Title");

            var button = new ButtonControl(_Name: "saveButton");
            button.Text = "Save";
            testDialog.Children.Add(button);

            var button2 = new ButtonControl(_Name: "saveButton2");
            button2.Text = "Save";
            button2.Size = new Cairo.PointD(150, 150);
            button2.IsAutoSize = false;
            testDialog.Children.Add(button2);

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

            testDialog.Children.Add(rect);

            return testDialog;
        }

        public override void Dispose()
        {
            dialog?.Dispose();
            dialog = null;

            uiManager?.Dispose();
            uiManager = null;

            // Leaving the patch in place across a world reload would keep a stale UIManager
            // reference alive and patch the next ClientMain as well.
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;

            base.Dispose();
        }
    }
}
