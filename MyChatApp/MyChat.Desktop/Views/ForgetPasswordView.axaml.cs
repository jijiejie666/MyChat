using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MyChat.Desktop.Views
{
    public partial class ForgetPasswordView : UserControl
    {
        public ForgetPasswordView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}