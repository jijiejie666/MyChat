using Avalonia.Controls;
using SukiUI.Controls; // 必须引用这个

namespace MyChat.Desktop.Views // 这里的命名空间必须和 XAML 里的 x:Class 一致
{
    public partial class MainWindow : SukiWindow
    {
        public MainWindow()
        {
            InitializeComponent();
        }
    }
}