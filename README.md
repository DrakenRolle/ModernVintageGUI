<h1>Hello there :)</h1> 

This is a approach, to fix the current GUI System for Vintage Story.
Core Element of this Framework is to provide a Stackcontainer based approach to structure and maintain the userinterface.

<h2>Goals of this Project</h2>

* Make a more Modder Friendly approch to making User Interfaces
* - Achieving this with making the Developer only define the Data he want to show and let this API handle Positioning, Sizing, Order.
  - Control focused approch like in other .NET UI Frameworks like, Winforms,WPF, UWP and so on.
* Dialog Scaling and Control Positioning should be working independent from Scale or Window Size
* <b>UI DESIGNER :D<b>
    * Not final thought but my idea was using the WPF Designer with Custom Controls, to you can create UIs in XAML with the WPF Designer to create Vintage Story User Interfaces
* either JSON or XML exportable Format
* easy to implement Custom Controls
* Long Term Sustainablity by decoupling this System as far as possible from the Vanilla one, so Game updates don't break all the User Interfaces
* Long Term thought: FixedPositioning reintegrated, but compatible with Designer

As of so far there are already some Systems in place but the whole thing needs some more work done to get it really going. 
So far only 2 Controls are implemented but the others should work too with the System. 
Also the Controls are only implemented barebone for default construction. 

<h2> What is included in here</h2>

* New Parenting and Positioning System not attached to the Bounds
* Autosizing of Controls based on Content (Containers from Children, (Basic)Textboxes from Textcontent and Font)
* Easy way of implementing new controls or port the ones from VS over
* Dynamic recomposing after UI was opened. So you can change and Edit the UI as you wish even if you already Opened the Dialog.
* UI works with GUI Scale properly
* decoupled rendering as the Vanilla System is used only where necessary 
* Margin and Padding calculation working properly
* You can still access the Vanilla GUI System as the new System still uses GUIElements and Bounds to generate the Data for the Composer internally.
* New Custom Parent Child System which is unbound to the Bounds System (pun not intended)


<h1> Code Examples and Screenshots </h1>

<h2>Create simple dialog from anywhere in your Code</h2>

            StaticTextElement myChildTextbox = new StaticTextElement("Hi im Fancy!", CairoFont.WhiteSmallishText());

            ContainerElement container = new ContainerElement(_Children: new ObservableCollection<UIControl> { myChildTextbox });

            ContainerElement maincontainer = new ContainerElement(_Children: new ObservableCollection<UIControl> { container });

            ControlTypes.DialogElement dialog = new ControlTypes.DialogElement(capi,maincontainer,"MyTestDialog");
            maincontainer.Dialog = dialog;

            dialog.TryOpen();           


<h2>Result</h2>

<img width="529" height="330" alt="grafik" src="https://github.com/user-attachments/assets/04ff12c7-0bc0-4233-b1fc-5a345805b542" />




<h2>Stacking controls and Texts</h2>


            StaticTextElement fancyChildText = new StaticTextElement("Hi im Fancy!", CairoFont.WhiteSmallishText());
            StaticTextElement moreFancyChildText = new StaticTextElement("Hi im more Fancy!!", CairoFont.WhiteSmallishText());

            ContainerElement container = new ContainerElement(_Children: new ObservableCollection<UIControl> { fancyChildText,moreFancyChildText });

            ContainerElement maincontainer = new ContainerElement(_Children: new ObservableCollection<UIControl> { container });

            ControlTypes.DialogElement dialog = new ControlTypes.DialogElement(capi,maincontainer,"MyTestDialog");
            maincontainer.Dialog = dialog;

            dialog.TryOpen();

<h2>Result</h2>

<img width="478" height="272" alt="grafik" src="https://github.com/user-attachments/assets/56fc442f-50b1-4db8-ba42-a988cfa9de9b" />

<h2>Stacking Horizontally</h2>

            StaticTextElement fancyChildText = new StaticTextElement("Hi im Fancy!", CairoFont.WhiteSmallishText());
            StaticTextElement moreFancyChildText = new StaticTextElement("Hi im more Fancy!!", CairoFont.WhiteSmallishText());

            ContainerElement verticalContainer = new ContainerElement(_Children: new ObservableCollection<UIControl> { fancyChildText,moreFancyChildText });

            StaticTextElement fancyChildTextHorizontally = new StaticTextElement("Hi im Fancy!", CairoFont.WhiteSmallishText());
            StaticTextElement moreFancyChildTextHorizontally = new StaticTextElement(" Hi im more Fancy!!", CairoFont.WhiteSmallishText());
            ContainerElement horizontalContainer = new ContainerElement(_Orientation:Orientation.Left,_Children: new ObservableCollection<UIControl> { fancyChildTextHorizontally, moreFancyChildTextHorizontally });

            ContainerElement maincontainer = new ContainerElement(_Children: new ObservableCollection<UIControl> { verticalContainer, horizontalContainer });

            ControlTypes.DialogElement dialog = new ControlTypes.DialogElement(capi,maincontainer,"MyTestDialog");
            maincontainer.Dialog = dialog;

            dialog.TryOpen();

<h2>In Detail</h2>

            StaticTextElement fancyChildTextHorizontally = new StaticTextElement("Hi im Fancy!", CairoFont.WhiteSmallishText());
            StaticTextElement moreFancyChildTextHorizontally = new StaticTextElement(" Hi im more Fancy!!", CairoFont.WhiteSmallishText());
            ContainerElement horizontalContainer = new ContainerElement(_Orientation:Orientation.Left,_Children: new ObservableCollection<UIControl> { fancyChildTextHorizontally, moreFancyChildTextHorizontally });
            
            
            ContainerElement maincontainer = new ContainerElement(_Children: new ObservableCollection<UIControl> { verticalContainer, horizontalContainer });


<h2>Result</h2>

<img width="770" height="292" alt="grafik" src="https://github.com/user-attachments/assets/fef3e15e-ed9e-4744-8b33-cc1eebf5a5b4" />


<h2>Horizontal Container inside the Vertical</h2>

            StaticTextElement fancyChildText = new StaticTextElement("Hi im Fancy!", CairoFont.WhiteSmallishText());
            StaticTextElement moreFancyChildText = new StaticTextElement("Hi im more Fancy!!", CairoFont.WhiteSmallishText());

            ContainerElement verticalContainer = new ContainerElement(_Children: new ObservableCollection<UIControl> { fancyChildText,moreFancyChildText });

            StaticTextElement fancyChildTextHorizontally = new StaticTextElement("Hi im Fancy!", CairoFont.WhiteSmallishText());
            StaticTextElement moreFancyChildTextHorizontally = new StaticTextElement(" Hi im more Fancy!!", CairoFont.WhiteSmallishText());
            ContainerElement horizontalContainer = new ContainerElement(_Orientation:Orientation.Left,_Children: new ObservableCollection<UIControl> { fancyChildTextHorizontally, moreFancyChildTextHorizontally });

            verticalContainer.Children.Add(horizontalContainer);

            ContainerElement maincontainer = new ContainerElement(_Children: new ObservableCollection<UIControl> { verticalContainer, horizontalContainer });

            ControlTypes.DialogElement dialog = new ControlTypes.DialogElement(capi,maincontainer,"MyTestDialog");
            maincontainer.Dialog = dialog;

            dialog.TryOpen();

<h2>In Detail</h2>

            StaticTextElement fancyChildTextHorizontally = new StaticTextElement("Hi im Fancy!", CairoFont.WhiteSmallishText());
            StaticTextElement moreFancyChildTextHorizontally = new StaticTextElement(" Hi im more Fancy!!", CairoFont.WhiteSmallishText());
            ContainerElement horizontalContainer = new ContainerElement(_Orientation:Orientation.Left,_Children: new ObservableCollection<UIControl> { fancyChildTextHorizontally, moreFancyChildTextHorizontally });

            verticalContainer.Children.Add(horizontalContainer);

            
<h2>Horizontal Container inside the Vertical after UI was already Composed</h2>

            StaticTextElement fancyChildText = new StaticTextElement("Hi im Fancy!", CairoFont.WhiteSmallishText());
            StaticTextElement moreFancyChildText = new StaticTextElement("Hi im more Fancy!!", CairoFont.WhiteSmallishText());

            ContainerElement verticalContainer = new ContainerElement(_Children: new ObservableCollection<UIControl> { fancyChildText,moreFancyChildText });

            StaticTextElement fancyChildTextHorizontally = new StaticTextElement("Hi im Fancy!", CairoFont.WhiteSmallishText());
            StaticTextElement moreFancyChildTextHorizontally = new StaticTextElement(" Hi im more Fancy!!", CairoFont.WhiteSmallishText());
            ContainerElement horizontalContainer = new ContainerElement(_Orientation:Orientation.Left,_Children: new ObservableCollection<UIControl> { fancyChildTextHorizontally, moreFancyChildTextHorizontally });


            ContainerElement maincontainer = new ContainerElement(_Children: new ObservableCollection<UIControl> { verticalContainer, horizontalContainer });

            ControlTypes.DialogElement dialog = new ControlTypes.DialogElement(capi,maincontainer,"MyTestDialog");
            maincontainer.Dialog = dialog;

            dialog.TryOpen();
            
            verticalContainer.Children.Add(horizontalContainer);

In Detail:

            dialog.TryOpen();
                                
            verticalContainer.Children.Add(horizontalContainer);


<h2>Result (Both results are the Same</h2>
<img width="688" height="335" alt="grafik" src="https://github.com/user-attachments/assets/8c02995e-454e-46e8-88e6-3037d99af6cb" />


<h2> You can of course alse Edit Content like Text and so on.</h2>

            fancyChildText.Text = "Hey don't touch my fancy Text!";

<h2>Access the GUIElement of the Control (only Possible after UI was opened</h2>

            GuiElementStaticText fancyChildTextGUIElement = fancyChildText.GetGUIElement<GuiElementStaticText>();
