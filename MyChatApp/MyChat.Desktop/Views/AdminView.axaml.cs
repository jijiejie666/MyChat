using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;



namespace MyChat.Desktop.Views
{
    public partial class AdminView : UserControl
    {
        public AdminView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}