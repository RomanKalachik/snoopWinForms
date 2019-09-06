using Snoop.Data;
using Snoop.Infrastructure;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Snoop
{
    public partial class SnoopUI : INotifyPropertyChanged
    {
        public static readonly RoutedCommand ClearSearchFilterCommand = new RoutedCommand("ClearSearchFilter", typeof(SnoopUI));
        public static readonly RoutedCommand CopyPropertyChangesCommand = new RoutedCommand("CopyPropertyChanges", typeof(SnoopUI));

        public static readonly DependencyProperty FPSColorProperty =
            DependencyProperty.Register("FPSColor", typeof(SolidColorBrush), typeof(SnoopUI));

        public static readonly DependencyProperty FPSProperty =
            DependencyProperty.Register("FPS", typeof(double), typeof(SnoopUI));
        public static readonly RoutedCommand HelpCommand = new RoutedCommand("Help", typeof(SnoopUI));
        public static readonly RoutedCommand InspectCommand = new RoutedCommand("Inspect", typeof(SnoopUI));
        public static readonly RoutedCommand IntrospectCommand = new RoutedCommand("Introspect", typeof(SnoopUI));
        public static readonly DependencyProperty PathDataProperty =
            DependencyProperty.Register("PathData", typeof(Geometry), typeof(SnoopUI));
        public static readonly DependencyProperty PercentElapsedTimeForCompositionProperty =
            DependencyProperty.Register("PercentElapsedTimeForComposition", typeof(double), typeof(SnoopUI));
        public static readonly RoutedCommand RefreshCommand = new RoutedCommand("Refresh", typeof(SnoopUI));
        public static readonly RoutedCommand SelectFocusCommand = new RoutedCommand("SelectFocus", typeof(SnoopUI));
        public static readonly RoutedCommand SelectFocusScopeCommand = new RoutedCommand("SelectFocusScope", typeof(SnoopUI));

        Queue<double> fpsData;
        bool isWaiting = false;
        System.Windows.Forms.Control target;

        DateTime timeSent;

        /// <summary>
        /// Indicates whether CurrentFocus should retur previously focused element.
        /// This fixes problem where Snoop steals the focus from snooped app.
        /// </summary>
        /// <summary>
        /// root is the object you are inspecting.
        /// </summary>
        /// <summary>
        /// rootVisualTreeItem is the VisualTreeItem for the root you are inspecting.
        /// </summary>
        private Timer updateTimer;

        private ObservableCollection<VisualTreeItem> visualTreeItems = new ObservableCollection<VisualTreeItem>();
        int watchdog = 10;

        static SnoopUI()
        {
        }
        public SnoopUI(System.Windows.Forms.Control control)
        {
            target = control;
            InitializeComponent();
            Title = string.Format("{0} {1}", Title, control);
            Loaded += this.SnoopUI_Loaded;
            fpsData = new Queue<double>();
            for (int i = 0; i < 100; i++) fpsData.Enqueue(0.0);

            this.InitializeComponent();

            try
            {
                PresentationTraceSources.Refresh();
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            }
            catch (NullReferenceException)
            {
            }

        }

        public event PropertyChangedEventHandler PropertyChanged;






        private void OnUpdateTimer(object sender)
        {
            UpdateCompositorOwnedData();
        }

        private void SnoopUI_Loaded(object sender, RoutedEventArgs e)
        {
        }

        /// <summary>
        /// Loop through the properties in the current PropertyGrid and save away any properties
        /// that have been changed by the user.  
        /// </summary>
        /// <param name="owningObject">currently selected object that owns the properties in the grid (before changing selection to the new object)</param>
        /// <summary>
        /// Cleanup when closing the window.
        /// </summary>
        /// <summary>
        /// Just for fun, the ability to run Snoop on itself :)
        /// </summary>
        /// <summary>
        /// Find the VisualTreeItem for the specified visual.
        /// If the item is not found and is not part of the Snoop UI,
        /// the tree will be adjusted to include the window the item is in.
        /// </summary>
        private void UnhandledExceptionHandler(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            if (SnoopModes.IgnoreExceptions)
            {
                return;
            }

            if (SnoopModes.SwallowExceptions)
            {
                e.Handled = true;
                return;
            }

            e.Handled = ErrorDialog.ShowDialog(e.Exception);
        }
        private void UpdateCompositorOwnedData()
        {
            if (isWaiting)
            {
                watchdog--;
                if (watchdog == 0) { isWaiting = false; watchdog = 10; }
                UpdatePathData(100, false);
                return;
            }
            timeSent = DateTime.Now;
            isWaiting = true;
            target.BeginInvoke(new Action(() =>
            {
                var timeRecieved = DateTime.Now;
                FPS = (int)(timeRecieved - timeSent).TotalMilliseconds;
                UpdatePathData(FPS);
                isWaiting = false;
            }));
        }



        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.DataContext = this;
            updateTimer = new Timer(OnUpdateTimer, null, 0, 100);
            base.OnInitialized(e);
        }

        protected void OnPropertyChanged(string propertyName)
        {
            Debug.Assert(this.GetType().GetProperty(propertyName) != null);
            if (this.PropertyChanged != null)
                this.PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
        protected void UpdatePathData(double fps, bool allowUpdate = true)
        {
            fpsData.Dequeue();
            fpsData.Enqueue(fps);
            StringBuilder sb = new StringBuilder();
            sb.Append("M 0,0 L 0,100 ");
            int counter = 0;
            foreach (var cfps in fpsData)
            {
                sb.Append(string.Format("{0},{1} ", counter++, 100 - Math.Min(100, cfps)));
            }
            if (allowUpdate)
                PathData = Geometry.Parse(sb.ToString());
        }

        public void DoEvents()
        {
            DispatcherFrame frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new DispatcherOperationCallback(ExitFrame), frame);
            Dispatcher.PushFrame(frame);
        }

        public object ExitFrame(object f)
        {
            ((DispatcherFrame)f).Continue = false;

            return null;
        }

        public static bool GoBabyGo(string settingsFile)
        {
            TransientSettingsData.LoadCurrent(settingsFile);

            return new CrossAppDomainSnoop().CrossDomainGoBabyGo(settingsFile);
        }

        public static bool GoBabyGoForCurrentAppDomain(string settingsFile)
        {
            try
            {
                TransientSettingsData.LoadCurrentIfRequired(settingsFile);

                Trace.WriteLine($"Running snoop in app domain \"{AppDomain.CurrentDomain.FriendlyName}\".");

                SnoopApplication();
                return true;
            }
            catch (Exception exception)
            {
                ErrorDialog.ShowDialog(exception, "Error Snooping", "There was an error snooping the application.", exceptionAlreadyHandled: true);
                return false;
            }
        }

        public static void SnoopApplication()
        {
            Trace.WriteLine("Snooping application.");
            var handle = new IntPtr(long.Parse(TransientSettingsData.Current.Handle));
            var control = System.Windows.Forms.Control.FromHandle(handle);
            SnoopUI sui = new SnoopUI(control);
            sui.Show();
        }

        public double FPS {
            get { return (double)GetValue(FPSProperty); }
            set { SetValue(FPSProperty, value); }
        }
        public SolidColorBrush FPSColor {
            get { return (SolidColorBrush)GetValue(FPSColorProperty); }
            set { SetValue(FPSColorProperty, value); }
        }
        public Geometry PathData {
            get { return (Geometry)GetValue(PathDataProperty); }
            set { SetValue(PathDataProperty, value); }
        }

        public double PercentElapsedTimeForComposition {
            get { return (double)GetValue(PercentElapsedTimeForCompositionProperty); }
            set { SetValue(PercentElapsedTimeForCompositionProperty, value); }
        }

    }


    public class PropertyValueInfo
    {
        public string PropertyName { get; set; }
        public object PropertyValue { get; set; }
    }

}