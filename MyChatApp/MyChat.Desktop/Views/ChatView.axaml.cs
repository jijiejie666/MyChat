using System;
using System.Threading.Tasks; // 引用 Task
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;     // 引用 Dispatcher
using MyChat.Desktop.ViewModels;

namespace MyChat.Desktop.Views
{
    public partial class ChatView : UserControl
    {
        // 用于缓存 ScrollViewer 控件，避免每次都查找
        private ScrollViewer? _scrollViewer;

        public ChatView()
        {

            InitializeComponent();
            var inputBox = this.FindControl<TextBox>("InputTextBox");
            if (inputBox != null)
            {
                inputBox.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // 当 DataContext (即 ViewModel) 发生变化时触发
        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is ChatViewModel vm)
            {
                // 1. 先取消订阅旧事件 (防止重复订阅或内存泄漏)
                vm.OnNewMessage -= ScrollToBottom;

                // 2. 重新订阅 ViewModel 的 "有新消息" 事件
                vm.OnNewMessage += ScrollToBottom;
            }
        }

        // ★★★ 修复重点：合并后的唯一 ScrollToBottom 方法 ★★★
        private void ScrollToBottom()
        {
            // 使用 UI 线程异步执行，防止阻塞
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                // 延迟 50ms，确保 UI 列表已经完成渲染和高度计算
                await Task.Delay(50);

                // 尝试查找或使用缓存的 ScrollViewer
                // 注意：XAML 中必须有 x:Name="MyScrollViewer"
                _scrollViewer ??= this.FindControl<ScrollViewer>("MyScrollViewer");

                // 执行滚动
                _scrollViewer?.ScrollToEnd();
            });
        }

        // 处理输入框按键逻辑 (Enter 发送，Shift+Enter 换行)
        private void OnInputKeyDown(object? sender, KeyEventArgs e)
        {
            // 1. 检查是否是 Enter 键
            if (e.Key == Key.Enter)
            {
                // 2. 检查是否按下了 Shift (Shift + Enter = 换行)
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    // 不处理，让事件继续传递给 TextBox，执行默认换行
                    return;
                }

                // 3. 单独按 Enter -> 发送
                var textBox = sender as TextBox;
                if (textBox != null && DataContext is ChatViewModel vm)
                {
                    // 强制同步最新文本（防止绑定延迟）
                    vm.InputText = textBox.Text ?? "";

                    // 执行发送
                    if (vm.SendCommand.CanExecute(null))
                    {
                        vm.SendCommand.Execute(null);

                        // ★★★ 关键：标记为已处理，阻止 TextBox 接收这个 Enter ★★★
                        // 这样 TextBox 就永远不知道你按了 Enter，也就不会换行了
                        e.Handled = true;
                    }
                }
            }
        }
    }
}