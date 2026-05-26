using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;

namespace NeuroSight.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
            // On launch, load the Setup screen into the empty ContentControl box
            MainContentArea.Content = new SetupView();
            UpdateSidebarUI(NavSetupBtn);
        }

        private void NavSetup_Click(object? sender, RoutedEventArgs e)
        {
            ScreenTitleText.Text = "Configuration Setup";
            MainContentArea.Content = new SetupView(); 
            UpdateSidebarUI(NavSetupBtn);
        }

        private void NavRun_Click(object? sender, RoutedEventArgs e)
        {
            ScreenTitleText.Text = "Live AI Inference";
            MainContentArea.Content = new RunView(); // We will create this below!
            UpdateSidebarUI(NavRunBtn);
        }

        private void UpdateSidebarUI(Button activeButton)
        {
            // Reset all sidebar buttons to transparent
            NavSetupBtn.Background = Brushes.Transparent;
            NavRunBtn.Background = Brushes.Transparent;
            NavOutputBtn.Background = Brushes.Transparent;

            // Highlight the clicked button
            activeButton.Background = new SolidColorBrush(Color.Parse("#2A2A30"));
        }

        private void BtnExit_Click(object? sender, RoutedEventArgs e)
        {
            Environment.Exit(0);
        }

        private void TopBarPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e); 
            }
        }
    }
}