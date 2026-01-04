using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MyChat.Desktop.Views
{
    public partial class CreateGroupView : UserControl
    {
        public CreateGroupView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}