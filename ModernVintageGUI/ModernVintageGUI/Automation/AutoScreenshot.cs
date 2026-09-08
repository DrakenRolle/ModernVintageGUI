using IS2Mod.ControlTypes;
using IS2Mod.ControlTypes.Custom;
using IS2Mod.Input;
using ModernVintageGUI.ControlTypes;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace ModernVintageGUI.Automation
{
    /// <summary>
    /// Drives the live game from a step script and photographs the result: open the showcase,
    /// pull a dropdown down, pop a menu, take a picture, leave.
    ///
    /// This is the in-game half of the screenshot pipeline. The other half is
    /// <c>ZIngameShots/ingame-screenshot.ps1</c>, which builds the mod, starts the game with the
    /// world and the environment variables below, and waits for the done file.
    ///
    /// The picture is the whole window, read back from the default framebuffer at the end of the
    /// frame exactly the way the game's own F12 does it. That is deliberate: a dropdown list or a
    /// context menu is a dialog of its own in the overlay band, not a child of the dialog it
    /// belongs to, so a picture of just the dialog's surface would not have it in it.
    ///
    /// Script format - one step per line, <c>#</c> starts a comment:
    /// <code>
    /// wait 90                # rendered frames; "wait 2s" or "wait 500ms" waits by the clock
    /// open                   # the showcase dialog, the same one the J hotkey opens
    /// shot showcase.png      # the whole window, into the output directory
    /// shot showcase.png crop # cut down to the dialogs on screen, popups included; "crop 24" leaves 24 pixels around them
    /// dropdown textDropdown  # opens the dropdown with that Name; "dropdown textDropdown close" closes it
    /// menu menuButton        # shows the context menu attached to that control; "menu menuButton close" hides it
    /// hover saveButton       # moves the cursor onto a control, for hover looks
    /// click saveButton       # presses and releases the left button in the middle of a control
    /// tab tabs 1             # switches the tabs control with that Name to its second page
    /// chat off               # hides the vanilla chat window - it sits where a popup hangs out of a centred dialog; "chat on" brings it back
    /// close                  # hides the showcase
    /// cmd /time set 12:00    # sends a chat line: a server command with "/", a client command with "."
    /// section pictures       # names the steps up to the next section line, for a run told to skip them
    /// onquit cmd /time set 6 # only when the script ends with quit: a teardown that a run keeping the game open skips
    /// quit                   # leaves the world - which saves it - and closes the game
    /// </code>
    ///
    /// Steps that change the UI run as main thread tasks, after the frame that asked for them,
    /// and the next step waits for that and for two more frames, so the change is on screen
    /// before a picture is taken.
    /// </summary>
    public sealed class AutoScreenshot : IRenderer, IDisposable
    {
        /// <summary>Environment variable naming the step script. Unset means: do nothing.</summary>
        public const string ScriptVariable = "MVGUI_AUTOSHOT";

        /// <summary>Environment variable naming the output directory. Defaults to the script's directory.</summary>
        public const string OutputVariable = "MVGUI_AUTOSHOT_OUT";

        /// <summary>Environment variable listing the sections to leave out, comma separated. Unset means: run everything.</summary>
        public const string SkipVariable = "MVGUI_AUTOSHOT_SKIP";

        /// <summary>Written into the output directory when the script has run: "ok" or "error: ...".</summary>
        public const string DoneFileName = "autoshot.done";

        /// <summary>Everything the run logged, also in the output directory - the pipeline prints it.</summary>
        public const string LogFileName = "autoshot.log";

        private const string LogPrefix = "[ModernVintageGUI autoshot] ";

        /// <summary>Frames to let a UI change settle before the next step. One would do; two is certain.</summary>
        private const int SettleFrames = 2;

        private readonly ICoreClientAPI capi;
        private readonly Func<CustomDialogElement> showShowcase;
        private readonly List<Step> steps;
        private readonly string outputDir;
        private readonly List<string> logLines = new List<string>();

        private CustomDialogElement? showcase;
        private HudDialogChat? hiddenChat;
        private int nextStep;
        private int waitFrames;
        private DateTime? waitUntil;
        private bool worldReady;
        private bool taskPending;
        private bool finished;
        private bool quitRequested;
        private bool disposed;

        /// <summary>The immersive first person setting as it was, put back when the run is through.</summary>
        private readonly bool immersiveBefore;
        private bool immersiveRestored;

        private readonly struct Step
        {
            public Step(int line, string verb, string[] args, bool onQuit, string? section)
            {
                Line = line;
                Verb = verb;
                Args = args;
                OnQuit = onQuit;
                Section = section;
            }

            public int Line { get; }
            public string Verb { get; }
            public string[] Args { get; }

            /// <summary>Written "onquit STEP": the step runs only in a script that ends the game.</summary>
            public bool OnQuit { get; }

            /// <summary>The "section NAME" line the step is under, if any - what a run can be told to skip.</summary>
            public string? Section { get; }

            public override string ToString() => (OnQuit ? "onquit " : "") + Verb + (Args.Length > 0 ? " " + string.Join(" ", Args) : "");
        }

        private AutoScreenshot(ICoreClientAPI capi, Func<CustomDialogElement> showShowcase, List<Step> steps, string outputDir, bool worldReady, IReadOnlyCollection<string> skipSections)
        {
            this.capi = capi;
            this.showShowcase = showShowcase;
            this.steps = steps;
            this.outputDir = outputDir;
            this.worldReady = worldReady;

            Directory.CreateDirectory(outputDir);

            // With the immersive first person mode on, the player's own body is drawn - and an
            // arm swings into the frame after every teleport. Off for the run, back afterwards.
            immersiveBefore = ClientSettings.ImmersiveFpMode;
            ClientSettings.ImmersiveFpMode = false;

            // Done and log files from the previous run would be read as this run's result.
            File.Delete(Path.Combine(outputDir, DoneFileName));
            File.Delete(Path.Combine(outputDir, LogFileName));

            if (!worldReady)
            {
                capi.Event.LevelFinalize += OnLevelFinalize;
            }

            // After the game's own screenshot system (2.0) - not that the two would meet, but the
            // order is documented that way and there is no reason to sit in front of it.
            capi.Event.RegisterRenderer(this, EnumRenderStage.Done, "mvgui-autoshot");

            // The sections the run was told to leave out - the pictures of a scene that is only
            // built to be walked around in - go first, a name that is in no section noted.
            foreach (string name in skipSections)
            {
                int left = steps.RemoveAll(s => string.Equals(s.Section, name, StringComparison.OrdinalIgnoreCase));
                Log(left > 0 ? "section '" + name + "' skipped, " + left + " step(s)" : "no section named '" + name + "' to skip");
            }

            // A script that does not end the game leaves what it built on screen to be looked
            // at, so the steps marked "onquit" - the teardown - are dropped rather than run.
            bool endsTheGame = steps.Exists(s => s.Verb == "quit");
            int dropped = endsTheGame ? 0 : steps.RemoveAll(s => s.OnQuit);

            Log("script with " + steps.Count + " step(s), output " + outputDir
                + (dropped > 0 ? "; " + dropped + " onquit step(s) skipped, the game stays open" : ""));
        }

        /// <summary>
        /// Starts a run if the environment asks for one, otherwise returns null. Called once when
        /// the client side of the mod starts, i.e. before the world is there - the run waits for
        /// it.
        /// </summary>
        public static AutoScreenshot? FromEnvironment(ICoreClientAPI capi, Func<CustomDialogElement> showShowcase)
        {
            string? scriptPath = Environment.GetEnvironmentVariable(ScriptVariable);

            if (string.IsNullOrWhiteSpace(scriptPath))
                return null;

            string? outputDir = Environment.GetEnvironmentVariable(OutputVariable);
            string? skip = Environment.GetEnvironmentVariable(SkipVariable);

            return Start(capi, showShowcase, scriptPath, outputDir, worldReady: false, SplitSections(skip));
        }

        /// <summary>"pictures, setup" or "pictures;setup" into the names; nothing for an empty list.</summary>
        public static string[] SplitSections(string? list)
        {
            if (string.IsNullOrWhiteSpace(list))
                return Array.Empty<string>();

            return list.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        /// <summary>
        /// Starts a run against a world that is already loaded - what the chat command does, so a
        /// script can be tried without restarting the game. <paramref name="skipSections"/> names
        /// the "section NAME" parts of the script to leave out.
        /// </summary>
        public static AutoScreenshot Start(ICoreClientAPI capi, Func<CustomDialogElement> showShowcase, string scriptPath, string? outputDir, bool worldReady, IReadOnlyCollection<string>? skipSections = null)
        {
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException("Step script not found", scriptPath);

            List<Step> steps = Parse(File.ReadAllLines(scriptPath));

            if (string.IsNullOrWhiteSpace(outputDir))
            {
                outputDir = Path.GetDirectoryName(Path.GetFullPath(scriptPath)) ?? Directory.GetCurrentDirectory();
            }

            return new AutoScreenshot(capi, showShowcase, steps, Path.GetFullPath(outputDir), worldReady, skipSections ?? Array.Empty<string>());
        }

        private static List<Step> Parse(string[] lines)
        {
            var steps = new List<Step>();
            string? section = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int comment = line.IndexOf('#');

                if (comment >= 0)
                    line = line.Substring(0, comment);

                string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length == 0)
                    continue;

                // "onquit STEP": the step is the rest of the line. Without one after it the line
                // is kept as it is and fails as its own step when its turn comes.
                bool onQuit = parts.Length > 1 && parts[0].Equals("onquit", StringComparison.OrdinalIgnoreCase);
                int first = onQuit ? 1 : 0;

                string[] args = new string[parts.Length - first - 1];
                Array.Copy(parts, first + 1, args, 0, args.Length);

                // "section NAME" is not a step: it names the ones after it, up to the next
                // section line, so a run can be told to leave them out. A name may recur.
                if (!onQuit && parts[0].Equals("section", StringComparison.OrdinalIgnoreCase) && args.Length == 1)
                {
                    section = args[0];
                    continue;
                }

                steps.Add(new Step(i + 1, parts[first].ToLowerInvariant(), args, onQuit, section));
            }

            return steps;
        }

        #region IRenderer
        public double RenderOrder => 3.0;

        public int RenderRange => 999;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (finished || disposed)
                return;

            if (!worldReady)
                return;

            // The world is finalized before the chunks around the player are there; until then
            // the game still shows its loading screen and a picture would be of that.
            if (capi.World is ClientMain game && !game.BlocksReceivedAndLoaded)
                return;

            if (taskPending)
                return;

            if (waitFrames > 0)
            {
                waitFrames--;
                return;
            }

            if (waitUntil.HasValue)
            {
                if (DateTime.UtcNow < waitUntil.Value)
                    return;

                waitUntil = null;
            }

            if (nextStep >= steps.Count)
            {
                Finish("ok");
                return;
            }

            Step step = steps[nextStep++];

            try
            {
                Execute(step);
            }
            catch (Exception e)
            {
                Log("line " + step.Line + " '" + step + "' failed: " + e);
                Finish("error: line " + step.Line + " '" + step + "': " + e.Message);
            }
        }
        #endregion

        #region Steps
        private void Execute(Step step)
        {
            Log("line " + step.Line + ": " + step);

            switch (step.Verb)
            {
                case "wait":
                    ParseWait(step);
                    break;

                case "open":
                    OnMainThread(() => showcase = showShowcase());
                    break;

                case "close":
                    OnMainThread(() => showcase?.Hide());
                    break;

                case "dropdown":
                    {
                        string name = Require(step, 0, "control name");
                        bool close = step.Args.Length > 1 && step.Args[1].Equals("close", StringComparison.OrdinalIgnoreCase);

                        OnMainThread(() =>
                        {
                            DropdownControl dropdown = Find(step, name) as DropdownControl
                                ?? throw new InvalidOperationException("'" + name + "' is not a dropdown");

                            if (close) dropdown.Close(); else dropdown.Open();
                        });
                        break;
                    }

                case "menu":
                    {
                        string name = Require(step, 0, "owner control name");
                        bool close = step.Args.Length > 1 && step.Args[1].Equals("close", StringComparison.OrdinalIgnoreCase);

                        OnMainThread(() =>
                        {
                            UIControl owner = Find(step, name);
                            ContextMenuControl? menu = null;

                            foreach (UIControl child in owner.Children)
                            {
                                if (child is ContextMenuControl found)
                                {
                                    menu = found;
                                    break;
                                }
                            }

                            if (menu == null)
                                throw new InvalidOperationException("'" + name + "' has no context menu attached");

                            if (close) menu.Hide(); else menu.Show();
                        });
                        break;
                    }

                case "hover":
                    {
                        string name = Require(step, 0, "control name");

                        OnMainThread(() =>
                        {
                            UIControl control = Find(step, name);
                            (CustomDialogElement dialog, int x, int y) = Center(control);
                            dialog.HandleMouseMove(new MouseEvent(x, y, 0, 0));
                        });
                        break;
                    }

                case "click":
                    {
                        string name = Require(step, 0, "control name");

                        OnMainThread(() =>
                        {
                            UIControl control = Find(step, name);
                            (CustomDialogElement dialog, int x, int y) = Center(control);
                            dialog.HandleMouseMove(new MouseEvent(x, y, 0, 0));
                            dialog.HandleMouseDown(new MouseEvent(x, y, EnumMouseButton.Left, 0));
                            dialog.HandleMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
                        });
                        break;
                    }

                case "shot":
                    {
                        string file = Require(step, 0, "file name");
                        bool crop = step.Args.Length > 1 && step.Args[1].Equals("crop", StringComparison.OrdinalIgnoreCase);
                        int margin = 0;

                        if (!crop && step.Args.Length > 1)
                            throw new InvalidOperationException("after the file name only 'crop [margin]' is understood");

                        if (crop && step.Args.Length > 2 && !int.TryParse(step.Args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out margin))
                            throw new InvalidOperationException("'" + step.Args[2] + "' is not a margin in pixels");

                        TakeScreenshot(file, crop, margin);
                        break;
                    }

                case "tab":
                    {
                        string name = Require(step, 0, "tabs control name");
                        string page = Require(step, 1, "page index");

                        if (!int.TryParse(page, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                            throw new InvalidOperationException("'" + page + "' is not a page index");

                        OnMainThread(() =>
                        {
                            TabsControl tabs = Find(step, name) as TabsControl
                                ?? throw new InvalidOperationException("'" + name + "' is not a tabs control");

                            tabs.Select(index);
                        });
                        break;
                    }

                case "chat":
                    {
                        string state = Require(step, 0, "on or off").ToLowerInvariant();

                        if (state != "on" && state != "off")
                            throw new InvalidOperationException("'chat' takes on or off");

                        OnMainThread(() => ShowChat(state == "on"));
                        break;
                    }

                case "cmd":
                    {
                        // The rest of the line, as the player would have typed it into the chat.
                        // A "/" goes to the server and comes back as world changes; a "." is a
                        // client command and runs here. Either way the next step waits two
                        // frames, and a script that needs the server's answer on screen waits
                        // longer itself.
                        string line = string.Join(" ", step.Args);

                        if (line.Length == 0)
                            throw new InvalidOperationException("'cmd' needs a chat line");

                        OnMainThread(() =>
                        {
                            if (line.StartsWith(".", StringComparison.Ordinal))
                                capi.TriggerChatMessage(line);
                            else
                                capi.SendChatMessage(line);
                        });
                        break;
                    }

                case "quit":
                    quitRequested = true;
                    Finish("ok");
                    break;

                case "onquit":
                    throw new InvalidOperationException("'onquit' needs a step after it");

                case "section":
                    throw new InvalidOperationException("'section' needs one name after it");

                default:
                    throw new InvalidOperationException("unknown step '" + step.Verb + "'");
            }
        }

        private void ParseWait(Step step)
        {
            string value = Require(step, 0, "frame count or duration").ToLowerInvariant();

            if (value.EndsWith("ms") && double.TryParse(value.Substring(0, value.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out double millis))
            {
                waitUntil = DateTime.UtcNow.AddMilliseconds(millis);
                return;
            }

            if (value.EndsWith("s") && double.TryParse(value.Substring(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
            {
                waitUntil = DateTime.UtcNow.AddSeconds(seconds);
                return;
            }

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int frames) && frames >= 0)
            {
                waitFrames = frames;
                return;
            }

            throw new InvalidOperationException("'" + value + "' is not a frame count, nor a duration like 2s or 500ms");
        }

        private static string Require(Step step, int index, string what)
        {
            if (step.Args.Length <= index)
                throw new InvalidOperationException("'" + step.Verb + "' needs a " + what);

            return step.Args[index];
        }

        /// <summary>
        /// Runs a UI change after this frame, on the main thread, and holds the next step until
        /// it has run and the change has been drawn. A dialog shown from inside a render callback
        /// would register its renderers while the game is walking its renderer lists.
        /// </summary>
        private void OnMainThread(Action action)
        {
            taskPending = true;

            capi.Event.EnqueueMainThreadTask(() =>
            {
                try
                {
                    action();
                    waitFrames = Math.Max(waitFrames, SettleFrames);
                }
                catch (Exception e)
                {
                    Log("step failed: " + e);
                    Finish("error: " + e.Message);
                }
                finally
                {
                    taskPending = false;
                }
            }, "mvgui-autoshot");
        }

        /// <summary>
        /// Reads the default framebuffer at the end of the frame - dialogs, popups, HUD, world -
        /// exactly as <c>SystemScreenshot</c> does for F12, into a file in the output directory.
        ///
        /// Cropped, the picture is cut down to the union of every dialog of ours that is on
        /// screen: the showcase and whatever hangs out of it, a dropdown list or a menu, since
        /// those are dialogs of their own. The margin is world around that, in pixels.
        /// </summary>
        private void TakeScreenshot(string fileName, bool crop, int margin)
        {
            if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                fileName += ".png";

            string fullPath = Path.Combine(outputDir, fileName);
            ClientPlatformAbstract platform = ScreenManager.Platform;

            platform.LoadFrameBuffer(EnumFrameBuffer.Default);

            try
            {
                if (!crop)
                {
                    platform.SaveScreenshot(outputDir, fullPath, withAlpha: false, ClientSettings.FlipScreenshot, null);
                    Log("saved " + fullPath);
                    return;
                }

                Size2i window = platform.WindowSize;

                using BitmapRef grab = platform.GrabScreenshot(window.Width, window.Height, scaleScreenshot: false, ClientSettings.FlipScreenshot, withAlpha: false);
                SKBitmap whole = ((BitmapExternal)grab).bmp;
                SKRectI rect = DialogBounds(margin, whole.Width, whole.Height);

                using var part = new SKBitmap(rect.Width, rect.Height, whole.ColorType, whole.AlphaType);

                if (!whole.ExtractSubset(part, rect))
                    throw new InvalidOperationException("could not cut " + rect + " out of the " + whole.Width + "x" + whole.Height + " frame");

                // The subset shares the frame's pixels with an offset; a copy of its own is what
                // the encoder gets.
                using SKBitmap cut = part.Copy();
                using SKImage image = SKImage.FromBitmap(cut);
                using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
                using FileStream file = File.Create(fullPath);
                png.SaveTo(file);

                Log("saved " + fullPath + " - " + rect.Width + "x" + rect.Height + " at " + rect.Left + "," + rect.Top);
            }
            finally
            {
                platform.UnloadFrameBuffer(EnumFrameBuffer.Default);
            }
        }

        /// <summary>
        /// The box around every visible dialog of ours, grown by the margin and kept inside the
        /// frame. With nothing of ours open it is the whole frame.
        /// </summary>
        private SKRectI DialogBounds(int margin, int frameWidth, int frameHeight)
        {
            double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
            int count = 0;

            if (UIManager.Current != null)
            {
                foreach (CustomDialogElement dialog in UIManager.Current.OpenDialogs)
                {
                    if (!dialog.IsVisible)
                        continue;

                    Cairo.PointD position = dialog.Position;
                    Cairo.PointD size = dialog.Size;

                    left = Math.Min(left, position.X);
                    top = Math.Min(top, position.Y);
                    right = Math.Max(right, position.X + size.X);
                    bottom = Math.Max(bottom, position.Y + size.Y);
                    count++;
                }
            }

            if (count == 0)
            {
                Log("nothing of ours is open, keeping the whole window");
                return SKRectI.Create(0, 0, frameWidth, frameHeight);
            }

            int x0 = Math.Max(0, (int)Math.Floor(left) - margin);
            int y0 = Math.Max(0, (int)Math.Floor(top) - margin);
            int x1 = Math.Min(frameWidth, (int)Math.Ceiling(right) + margin);
            int y1 = Math.Min(frameHeight, (int)Math.Ceiling(bottom) + margin);

            return SKRectI.Create(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
        }

        /// <summary>
        /// The vanilla chat window sits at the bottom left, which is where a list or a menu
        /// hanging out of a centred dialog lands, and it answers TryClose with false. It does
        /// have DoClose - what Escape ends up calling - and TryOpen brings it back. A run that
        /// leaves the game open puts it back itself.
        /// </summary>
        private void ShowChat(bool visible)
        {
            if (!visible)
            {
                if (hiddenChat != null)
                    return;

                foreach (GuiDialog gui in new List<GuiDialog>(capi.Gui.OpenedGuis))
                {
                    if (gui is HudDialogChat chat)
                    {
                        chat.DoClose();
                        hiddenChat = chat;
                        break;
                    }
                }
            }
            else if (hiddenChat != null)
            {
                hiddenChat.TryOpen();
                hiddenChat = null;
            }
        }
        #endregion

        #region Control lookup
        /// <summary>
        /// Finds a control by Name in the showcase, or a menu entry by its text - menu entries
        /// live in a popup of their own once shown, but the menu that owns them is a child of its
        /// anchor control, so they are reachable through it.
        /// </summary>
        private UIControl Find(Step step, string name)
        {
            if (showcase == null)
                throw new InvalidOperationException("nothing is open - put 'open' before line " + step.Line);

            return FindIn(showcase, name) ?? throw new InvalidOperationException("no control named '" + name + "'");
        }

        private static UIControl? FindIn(UIControl root, string name)
        {
            if (root.Name == name)
                return root;

            if (root is ContextMenuItem item && item.Text == name)
                return item;

            foreach (UIControl child in root.Children)
            {
                UIControl? found = FindIn(child, name);
                if (found != null)
                    return found;
            }

            if (root is ContextMenuControl menu)
            {
                foreach (ContextMenuItem entry in menu.Items)
                {
                    UIControl? found = FindIn(entry, name);
                    if (found != null)
                        return found;
                }
            }

            if (root is ContextMenuItem parent)
            {
                foreach (ContextMenuItem entry in parent.ChildItems)
                {
                    UIControl? found = FindIn(entry, name);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        private static (CustomDialogElement dialog, int x, int y) Center(UIControl control)
        {
            CustomDialogElement dialog = control.Dialog
                ?? throw new InvalidOperationException("'" + control.Name + "' is not in a shown dialog");

            Cairo.PointD position = control.GetScreenPosition();

            return (dialog, (int)(position.X + control.Size.X / 2), (int)(position.Y + control.Size.Y / 2));
        }
        #endregion

        #region Finishing
        private void OnLevelFinalize()
        {
            worldReady = true;
            Log("world ready");
        }

        private void Finish(string status)
        {
            if (finished)
                return;

            finished = true;
            Log("finished: " + status + (quitRequested ? ", leaving the world" : ", leaving the game open"));

            WriteDone(status);
            RestoreImmersiveMode();

            if (quitRequested)
            {
                Quit();
            }
            else if (hiddenChat != null)
            {
                // Whoever looks at the open game afterwards wants their chat back.
                capi.Event.EnqueueMainThreadTask(() => ShowChat(true), "mvgui-autoshot-chat");
            }
        }

        private void RestoreImmersiveMode()
        {
            if (immersiveRestored)
                return;

            immersiveRestored = true;
            ClientSettings.ImmersiveFpMode = immersiveBefore;
        }

        /// <summary>
        /// Written whole and then moved into place, so the pipeline never reads half a file.
        /// </summary>
        private void WriteDone(string status)
        {
            try
            {
                string donePath = Path.Combine(outputDir, DoneFileName);
                string tempPath = donePath + ".tmp";

                File.WriteAllText(tempPath, status + Environment.NewLine);
                File.Move(tempPath, donePath, overwrite: true);
            }
            catch (Exception e)
            {
                capi.Logger.Error(LogPrefix + "could not write the done file: " + e);
            }
        }

        /// <summary>
        /// Leaves the world the way the escape menu's button does - which stops the single player
        /// server and saves - and closes the window once the server is gone. Closing it earlier
        /// would be the same as killing the process while it saves.
        /// </summary>
        private void Quit()
        {
            capi.Event.EnqueueMainThreadTask(() =>
            {
                if (capi.World is ClientMain game)
                {
                    game.SendLeave(0);
                    game.exitReason = "autoshot finished";
                    game.DestroyGameSession(gotDisconnected: false, EnumExitMode.SoftExit);
                }
            }, "mvgui-autoshot-leave");

            // Static state only from here on: leaving the world disposes the mod, and with it
            // this object.
            var closer = new Thread(() =>
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(90);

                // Give the leave above a moment to reach the server before asking whether it
                // has stopped.
                Thread.Sleep(1000);

                while (ScreenManager.Platform.IsServerRunning && DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(200);
                }

                Thread.Sleep(500);

                ScreenManager.EnqueueMainThreadTask(() =>
                    ScreenManager.Platform.WindowExit("autoshot finished", EnumExitMode.SoftExit));
            })
            {
                IsBackground = true,
                Name = "mvgui-autoshot-closer"
            };

            closer.Start();
        }

        private void Log(string message)
        {
            capi.Logger.Notification(LogPrefix + message);

            lock (logLines)
            {
                logLines.Add(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + message);

                try
                {
                    File.WriteAllLines(Path.Combine(outputDir, LogFileName), logLines);
                }
                catch
                {
                    // The game log has the line as well.
                }
            }
        }
        #endregion

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            capi.Event.LevelFinalize -= OnLevelFinalize;
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Done);
            RestoreImmersiveMode();

            // A run that never got to finish - the world was left under it - still has to report.
            if (!finished)
            {
                finished = true;
                WriteDone("error: the world was left before the script finished");
            }
        }
    }
}
