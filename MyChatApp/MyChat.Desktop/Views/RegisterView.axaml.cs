using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MyChat.Desktop.Views
{
    public partial class RegisterView : UserControl
    {
        public RegisterView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}