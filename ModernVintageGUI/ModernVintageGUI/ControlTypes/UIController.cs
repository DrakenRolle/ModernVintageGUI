using Cairo;
using IS2Mod.Enums;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using Vintagestory.API.Client;

namespace IS2Mod.ControlTypes
{
    public abstract class UIControl : INotifyPropertyChanged
    {
        #region Properties
        private ObservableCollection<UIControl> m_Children = new ObservableCollection<UIControl>();
        public ObservableCollection<UIControl> Children
        {
            get { return m_Children; }
            set { m_Children = value; OnPropertyChanged(); }
        }
        
        private UIControl m_Parent;
        public UIControl Parent
        {
            get { return m_Parent; }
            set { m_Parent = value; OnPropertyChanged(); }
        }

        private DialogElement m_Dialog;
        public DialogElement Dialog
        {
            get { return m_Dialog; }
            set { m_Dialog = value; OnPropertyChanged(); }
        }

        private PointD m_Position;
        public PointD Position
        {
            get { return m_Position; }
            set { m_Position = value; OnPropertyChanged(); }
        }

        private bool m_IsAutoSize;
        public bool IsAutoSize
        {
            get { return m_IsAutoSize; }
            set { 
                m_IsAutoSize = value; OnPropertyChanged();
            }
        }

        private double m_Margin= 0;
        public double Margin
        {
            get { return m_Margin; }
            set { m_Margin = value; OnPropertyChanged(); }
        }

        private double m_Padding = 0;
        public double Padding
        {
            get { return m_Padding; }
            set { m_Padding = value; OnPropertyChanged(); }
        }

        private PointD m_Size = new PointD(0, 0);
        public PointD Size
        {
            get { return m_Size; }
            set { m_Size = value; OnPropertyChanged();
            }
        }

        private string[] m_DynamicSize= null;
        public string[] DynamicSize
        {
            get { return m_DynamicSize; }
            set { m_DynamicSize = value; OnPropertyChanged(); }
        }

        private bool m_IsStaticElement = false;
        public bool IsStaticElement
        {
            get { return m_IsStaticElement; }
            set { m_IsStaticElement = value; OnPropertyChanged(); }
        }

        private int m_Index = 0;
        public int Index
        {
            get { return m_Index; }
            set { m_Index = value; OnPropertyChanged(); }
        }

        private Orientation m_InsideOrientation;
        public Orientation InsideOrientation
        {
            get { return m_InsideOrientation; }
            set { m_InsideOrientation = value; OnPropertyChanged(); }
        }

        private GuiElement m_ControlGuiElement;
        public GuiElement ControlGuiElement
        {
            get { return m_ControlGuiElement; }
        }

        private ElementBounds m_Bounds;
        public ElementBounds Bounds
        {
            get { return m_Bounds; }
        }

        private string m_Name;
        public string Name
        {
            get { return m_Name; }
            set { m_Name = value; OnPropertyChanged(); }
        }

        private GuiComposer m_Composer;
        public GuiComposer Composer
        {
            get { return m_Composer; }
            set { m_Composer = value; OnPropertyChanged(); }
        }
        #endregion 

        #region Abstract Function Defintions
        /// <summary>
        /// Default Function to create an GUIElement. Every ControlType has this Embedded.
        /// </summary>
        /// <returns></returns>
        public abstract GuiElement Compose();

        #endregion

        #region only private Members
        private bool isMultiCompose = false;
        #endregion

        #region Eventhandlers (Probably there is more coming)
        public event PropertyChangedEventHandler PropertyChanged;
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor usally used for the MainContainer. 
        /// </summary>
        public UIControl() 
        {
            IsAutoSize = true;
        }

        /// <summary>
        /// Default Construtor for everything else
        /// </summary>
        /// <param name="_Name">Name of the Poperty. Can be set, so you can grab it out of Children Collection easier, but is not requiered for the System to work.</param>
        /// <param name="_Size">The Size of the UI Control Element, without Padding or Margin! If Value is either not set or set to X=0 & Y=0 the Control is set as Autoscaling.</param>
        /// <param name="_Orientation">The Orientation of the Children inside the Control. Keep in Mind, that right now only Containers can have this Value set to something otherwise, than Orientation.None.</param>
        /// <param name="_Margin">The Margin of the Control. So outer Spacing between this Control and its Parent/other Sibling. Keep in Mind, that later for positioning of the Control, the Padding of the Parent and the Margin of the other Children will also be calculated.</param>
        /// <param name="_Padding">The Margin of the Control. So outer Spacing between this Control and its Children. Keep in Mind, that later for positioning of the Control, the Margin of Children also gets calculated.</param>
        /// <param name="_Index">The Sortingindex in the Parents Children Collection. If you want to set a Control to the very first position, this has to be the lowest value. If the index is not set, the controls get sorted by First In, First Rendered.</param>
        /// <param name="_Children">The Childcontrols inside this Control. CAN ONLY BE USED BY CONTAINERS!</param>
        protected UIControl(string _Name = "", PointD? _Size = null, Orientation _Orientation = Orientation.None, double _Margin = 0, double _Padding = 0, int _Index = 0, ObservableCollection<UIControl> _Children = null)
        {
            m_Margin = _Margin;
            m_Padding = _Padding;
            m_Index = _Index;
            m_InsideOrientation = _Orientation;
            m_Name = _Name;
            if (_Children != null)
            {
                m_Children = _Children;
                m_Children.ToList().ForEach(x => x.Parent = this);
            }
            
            if (_Size.HasValue)
            {
                m_Size = _Size.Value;
                if(m_Size.X == 0 && m_Size.Y == 0)
                {
                    m_IsAutoSize = true;
                }
                else
                {
                    m_IsAutoSize = false;
                }
            }
            else
            {
                m_IsAutoSize = true;

            }
            Children.CollectionChanged += Children_CollectionChanged;
        }
        /// <summary>
        /// Default Construtor for everything else BUT WITH RELATIVE SCALE!!! This right now only WIP and not working.
        /// </summary>
        /// <param name="_Name">Name of the Poperty. Can be set, so you can grab it out of Children Collection easier, but is not requiered for the System to work.</param>
        /// <param name="_Size">The Size of the UI Control Element, without Padding or Margin! THIS IS FOR DYNAMIC SCALING WITH WILDCARDS! If Value is either not set or both empty String the Control is set as Autoscaling. 
        /// USAGE: Size = {"0.3*","*"} 
        /// means the Control takes one third of the Parents space available, after all fixed Size Controls (Size != null or X != 0 and Y != 0) or Autosized ContentElements (eg: StaticTextElements).
        /// and it takes all the Height available after fixed Size Controls,Autosized Contentelements AND Portionsized (eg: 0.3*) got calculated. If more then one has Height to * then it will split the space.
        /// </param>
        /// <param name="_Orientation">The Orientation of the Children inside the Control. Keep in Mind, that right now only Containers can have this Value set to something otherwise, than Orientation.None.</param>
        /// <param name="_Margin">The Margin of the Control. So outer Spacing between this Control and its Parent/other Sibling. Keep in Mind, that later for positioning of the Control, the Padding of the Parent and the Margin of the other Children will also be calculated.</param>
        /// <param name="_Padding">The Margin of the Control. So outer Spacing between this Control and its Children. Keep in Mind, that later for positioning of the Control, the Margin of Children also gets calculated.</param>
        /// <param name="_Index">The Sortingindex in the Parents Children Collection. If you want to set a Control to the very first position, this has to be the lowest value. If the index is not set, the controls get sorted by First In, First Rendered.</param>
        /// <param name="_Children">The Childcontrols inside this Control. CAN ONLY BE USED BY CONTAINERS!</param>
        protected UIControl(string _Name = "", string[] _Size = null, Orientation _Orientation = Orientation.None, double _Margin = 0, double _Padding = 0, int _Index = 0, ObservableCollection<UIControl> _Children = null)
        {
            throw new NotImplementedException("This Stuff not ready (yet!).");
            m_Margin = _Margin;
            m_Padding = _Padding;
            m_Index = _Index;
            m_InsideOrientation = _Orientation;
            m_Name = _Name;
            if (_Children != null)
            {
                m_Children = _Children;
                m_Children.ToList().ForEach(x => x.Parent = this);
            }
            if (_Size != null && string.IsNullOrEmpty(_Size[0]) && string.IsNullOrEmpty(_Size[1]))
            {
                m_DynamicSize = _Size;
                m_IsAutoSize = false;
            }
        }
        #endregion

        #region User Helpfunctions
        /// <summary>
        /// For the UI to Update to Changes to a GUI Element you have to use this function. Otherwise the changes to it will not be recognised.
        /// </summary>
        public void SetOrUpdateGUIElement(Action<GuiElement> changes)
        {
            changes.Invoke(m_ControlGuiElement);
            RecomposeToMain();
        }
        /// <summary>
        /// For the UI to Update to Changes to a Bounds you have to use this function. Otherwise the changes to it will not be recognised.
        /// </summary>
        public void SetOrUpdateBounds(Action<ElementBounds> changes)
        {
            changes.Invoke(m_Bounds);
            RecomposeToMain();
        }

        /// <summary>
        /// This Function can be used to change multiple informations inside the UIControl Data with triggering a compose only after all changes were made.
        /// </summary>
        public void MultiChangeCompose(Action expression)
        {
            isMultiCompose = true;
            expression.Invoke();
            isMultiCompose = false;
            RecomposeToMain();
        }

        /// <summary>
        /// You can use the normal Property to get the GUI Element, this just a Helper, to also convert it to your Expected Type.
        /// THIS LOGIC COULD BE CHANGED LATER ON AS ITS ALSO NOT HARD EMBEDDED AND HAS ITS FLAWS TOO.
        /// </summary>
        /// <typeparam name="T">The Type the GUIElement should have.</typeparam>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public virtual T GetGUIElement<T>() where T : GuiElement
        {
            T retVal = null;
            try
            {
                retVal = (T)ControlGuiElement;
            }
            catch (Exception ex)
            {
                Type guiElementType = typeof(GuiElement);
                if (ControlGuiElement != null)
                {
                    guiElementType = ControlGuiElement.GetType();
                }

                throw new Exception($"Wrong Type passed for {nameof(GetGUIElement)}. Expected {guiElementType.Name}");
            }
            return retVal;
        }
        #endregion

        #region internalEvents
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            RecomposeToMain();
        }
        private void Children_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (var cur in e.NewItems)
                {
                    if (cur.GetType().IsOrInheritsFrom(typeof(UIControl)) && cur != null)
                    {
                        UIControl control = (UIControl)cur;
                        if (control != null)
                        {
                            control.Parent = this;
                        }
                    }

                }
            }
            if (e.OldItems != null)
            {
                foreach (var cur in e.OldItems)
                {
                    if (cur.GetType().IsOrInheritsFrom(typeof(UIControl)) && cur != null)
                    {
                        UIControl control = (UIControl)cur;
                        if (control != null)
                        {
                            control.Parent = null;
                        }
                    }

                }
            } 
            RecomposeToMain();

        }
        #endregion

        #region Compose and Compile Logic
        /// <summary>
        /// Function to Notify the Main Control of the UI to redraw all its Rendering Data (Size,Bounds,GUIElements and so on). Happens usally on Data changes
        /// </summary>
        public void RecomposeToMain()
        {
            if (Composer != null && Composer.Composed && !isMultiCompose)
            {
                if (Parent == null)
                {
                    Composer.Composed = false;
                    Dialog.ComposeDialog();
                }
                else
                {

                    Parent.RecomposeToMain();
                }
            }

        }

        /// <summary>
        /// Calulates all the Data for Composing the GUI.
        /// </summary>
        /// <param name="childs">All the Child Elements of this Control.</param>
        /// <param name="firstRun">If the Main Control invokes this Function it has to run with FirstRun = true</param>
        public void CompileAllObjects(ObservableCollection<UIControl> childs = null, bool firstRun = true)
        {
            if (this.Parent == null && firstRun)
            {
                CalculateSize();
                CalculateAllPositions();
                m_ControlGuiElement = CalcGuiElementWithBounds(Composer);
                childs = this.Children;
            }
            if (childs != null)
            {
                foreach (var Child in childs)
                {
                    Child.m_ControlGuiElement = Child.CalcGuiElementWithBounds(Composer);
                    CompileAllObjects(Child.Children, false);
                }
                if (firstRun)
                {
                    CompileToComposerRecursive();
                }
            }

        }

        /// <summary>
        /// Due to us not using the Bounds Parent System, the GUIElements get just flat Added to the Composer, this way we kinda use it just as a "Drawing" Logic, while structure gets handled by the UIControllers System
        /// </summary>
        public void CompileToComposerRecursive()
        {
            if (IsStaticElement)
            {
                Composer.AddStaticElement(ControlGuiElement);
            }
            else
            {
                Composer.AddInteractiveElement(ControlGuiElement);
            }

            foreach (var item in Children)
            {
                item.CompileToComposerRecursive();
            }

        }

        /// <summary>
        /// Builds the bound for the Control. Invokes the ControlTypes specific Compose Function, it has to override.
        /// </summary>
        /// <param name="composer"></param>
        /// <returns>Gives the ready to use Control with set Bounds back.</returns>
        public GuiElement CalcGuiElementWithBounds(GuiComposer composer)
        {
            GuiElement temp = null;
            Composer = composer;
            ElementBounds bound = ElementBounds.Fixed(Position.X, Position.Y, Size.X, Size.Y);

            this.m_Bounds = bound;

            temp = Compose();
            m_ControlGuiElement = temp;

            return temp;
        }

        /// <summary>
        /// Calculates the Size and correct Positioning for the Control based on all the Factors like Parent Padding, Sibling Margin, Own Margin its Size and so on.
        /// </summary>
        /// <returns></returns>
        public virtual PointD CalculateSize()
        {
            if (!IsAutoSize)
            {
                return new PointD(Size.X, Size.Y);
            }

            PointD calculatedSize = new PointD(0, 0);

            foreach (UIControl child in Children)
            {
                PointD childSize = child.IsAutoSize ? child.CalculateSize() : child.Size;

                PointD childSizeWithSpacing = GetChildSizeWithMarginPadding(child, childSize);

                calculatedSize = MergeSizeByOrientation(childSizeWithSpacing, calculatedSize);
            }

            Size = calculatedSize;
            return calculatedSize;
        }

        /// <summary>
        /// Merges to PointD's based on the Orientation. 
        /// Basicly what happens is: 
        /// If Orientation is Orientation.Top or Orientation.Bottom, the Width will be set to the Biggest value of those to, as the Container scales with the Value. While the Height can be Set to a relative or Fixed Value.
        /// Orientation is Orientation.Left or Orientation.Right, the Height will be set to the Biggest value of those to, as the Container scales with the Value. While the Width can be Set to a relative or Fixed Value.
        /// </summary>
        /// <param name="addSize"></param>
        /// <param name="currentSize"></param>
        /// <returns></returns>
        private PointD MergeSizeByOrientation(PointD addSize, PointD currentSize)
        {
            PointD retVal = new PointD(currentSize.X, currentSize.Y);

            if (InsideOrientation == Orientation.Top || InsideOrientation == Orientation.Bottom)
            {
                retVal.Y += addSize.Y;
                retVal.X = Math.Max(retVal.X, addSize.X);
            }
            else if (InsideOrientation == Orientation.Left || InsideOrientation == Orientation.Right)
            {
                retVal.X += addSize.X;
                retVal.Y = Math.Max(retVal.Y, addSize.Y);
            }
            else if (InsideOrientation == Orientation.None)
            {
                retVal.X = Math.Max(retVal.X, addSize.X);
                retVal.Y = Math.Max(retVal.Y, addSize.Y);
            }

            return retVal;
        }

        /// <summary>
        /// Calculates all the Bounderies of the Control.
        /// </summary>
        /// <param name="child"></param>
        /// <param name="childSize"></param>
        /// <returns></returns>
        private PointD GetChildSizeWithMarginPadding(UIControl child, PointD childSize)
        {
            double totalMargin = 2 * child.Margin;
            double totalPadding = 2 * this.Padding;

            return new PointD(
                childSize.X + totalMargin + totalPadding,
                childSize.Y + totalMargin + totalPadding
            );
        }

        /// <summary>
        /// Calculates all Postions based on the Rules explained on the <see cref="MergeSizeByOrientation"/> Method./>
        /// </summary>
        public void CalculateAllPositions()
        {
            if (Parent == null)
            {
                Position = new PointD(0, 0);
            }

            for (int i = 0; i < Children.Count; i++)
            {
                UIControl previousSibling = i > 0 ? Children[i - 1] : null;
                Children[i].CalculatePosition(previousSibling);

                Children[i].CalculateAllPositions();
            }
        }

        /// <summary>
        /// Calculates the Position on this Control, relative to its Sibling. 
        /// Also the Backbone function for the Position Calculation
        /// </summary>
        /// <param name="previousSibling">the previous <see cref="UIControl"/> Sibling or in the Case of Main Control, <see cref="null"/></param>
        private void CalculatePosition(UIControl previousSibling)
        {
            if (Parent == null)
            {
                Position = new PointD(0, 0);
                return;
            }

            double startX = Parent.Position.X + Parent.Padding + Margin;
            double startY = Parent.Position.Y + Parent.Padding + Margin;

            if (previousSibling != null)
            {
                switch (Parent.InsideOrientation)
                {
                    case Orientation.Top:
                    case Orientation.Bottom:
                        startX = Parent.Position.X + Parent.Padding + Margin;
                        startY = previousSibling.Position.Y + previousSibling.Size.Y + previousSibling.Margin + Margin;
                        break;

                    case Orientation.Left:
                    case Orientation.Right:
                        startX = previousSibling.Position.X + previousSibling.Size.X + previousSibling.Margin + Margin;
                        startY = Parent.Position.Y + Parent.Padding + Margin;
                        break;

                    case Orientation.None:
                        startX = Parent.Position.X + Parent.Padding + Margin;
                        startY = Parent.Position.Y + Parent.Padding + Margin;
                        break;
                }
            }

            Position = new PointD(startX, startY);

            double parentMaxX = Parent.Position.X + Parent.Size.X - Parent.Padding;
            double parentMaxY = Parent.Position.Y + Parent.Size.Y - Parent.Padding;

            double clippedWidth = Size.X;
            double clippedHeight = Size.Y;

            if (Position.X + Size.X > parentMaxX)
            {
                clippedWidth = Math.Max(0, parentMaxX - Position.X);
            }

            if (Position.Y + Size.Y > parentMaxY)
            {
                clippedHeight = Math.Max(0, parentMaxY - Position.Y);
            }

            double parentMinX = Parent.Position.X + Parent.Padding;
            double parentMinY = Parent.Position.Y + Parent.Padding;

            double finalX = startX;
            double finalY = startY;

            if (startX < parentMinX)
            {
                clippedWidth = Math.Max(0, clippedWidth - (parentMinX - startX));
                finalX = parentMinX;
            }

            if (startY < parentMinY)
            {
                clippedHeight = Math.Max(0, clippedHeight - (parentMinY - startY));
                finalY = parentMinY;
            }

            Position = new PointD(finalX, finalY);
            Size = new PointD(clippedWidth, clippedHeight);
        }
        #endregion
    }
}
