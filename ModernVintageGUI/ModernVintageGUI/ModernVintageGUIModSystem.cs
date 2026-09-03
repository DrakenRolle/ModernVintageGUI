using HarmonyLib;
using IS2Mod.ControlTypes;
using IS2Mod.ControlTypes.Custom;
using IS2Mod.Input;
using ModernVintageGUI.ControlTypes;
using ModernVintageGUI.Enums;
using System.Collections.Generic;
using System.Linq;
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

            // The title bar has to reach the edges the way vanilla does, so the dialog itself
            // gets no padding and the content below the bar sits in a padded container instead.
            testDialog.Padding = 0;

            var titleBar = new TitleBarControl("My Title") { Name = "titleBar" };
            testDialog.Children.Add(titleBar);

            var content = new RectangleControl(_Name: "content");
            content.InsideOrientation = IS2Mod.Enums.Orientation.Top;
            content.Padding = 10;
            testDialog.Children.Add(content);

            var button = new ButtonControl(_Name: "saveButton");
            button.Text = "Save";
            content.Children.Add(button);

            AttachTestContextMenu(capi, button);

            var button2 = new ButtonControl(_Name: "saveButton2");
            button2.Text = "Save";
            button2.Size = new Cairo.PointD(150, 150);
            button2.IsAutoSize = false;
            content.Children.Add(button2);

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

            content.Children.Add(rect);

            return testDialog;
        }

        /// <summary>
        /// Test menu for the context menu work: the same two entries the vanilla dialog title bar
        /// offers, so the look can be compared against the original side by side.
        ///
        /// The menu adds itself to the button in its constructor and sits there as a zero sized
        /// anchor - the button keeps its size and everything below it stays where it was.
        /// </summary>
        private static void AttachTestContextMenu(ICoreClientAPI capi, ButtonControl button)
        {
            // Third entry with children, to exercise the cascade.
            var moreItem = new ContextMenuItem("More", new List<ContextMenuItem>
            {
                new ContextMenuItem("Text 1"),
                new ContextMenuItem("Text 2"),
                new ContextMenuItem("Text 3")
            });

            var menu = new ContextMenuControl(
                button,
                new List<ContextMenuItem>
                {
                    new ContextMenuItem("Fixed"),
                    new ContextMenuItem("Movable"),
                    moreItem
                },
                "positionMode",
                ContextMenuAnchor.BottomLeft);

            // One subscription for the whole cascade - picks from the sub menu bubble up here
            // too, so no reference to any single entry has to be kept around.
            menu.ItemActivated += (sender, e) =>
            {
                capi.ShowChatMessage("Context menu: " + string.Join(" > ", e.Path.Select(i => i.Text)));
            };

            // Clicking the button again while the menu is open does not reach this handler: the
            // UIManager consumes that click to dismiss the menu, which is what makes it a toggle.
            button.Clicked += (sender, e) => menu.Toggle();
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
