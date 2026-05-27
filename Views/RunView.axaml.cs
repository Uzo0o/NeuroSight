using Avalonia.Controls;
using NeuroSight.ViewModels;

namespace NeuroSight.Views
{
    public partial class RunView : UserControl
    {
        public RunView()
        {
            InitializeComponent();
            DataContext = new RunViewModel();
        }
    }
}
