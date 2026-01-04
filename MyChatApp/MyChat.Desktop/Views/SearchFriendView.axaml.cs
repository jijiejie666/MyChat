using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MyChat.Desktop.Views
{
    public partial class SearchFriendView : UserControl
    {
        public SearchFriendView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}