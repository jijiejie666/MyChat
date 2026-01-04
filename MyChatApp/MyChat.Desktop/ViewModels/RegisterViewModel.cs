using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyChat.Client.Core;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace MyChat.Desktop.ViewModels
{
    public partial class RegisterViewModel : ViewModelBase
    {
        [ObservableProperty] private string _account;
        [ObservableProperty] private string _password;
        [ObservableProperty] private string _nickname;
        [ObservableProperty] private string _statusMessage;
        [ObservableProperty] private bool _isBusy;

        // 定义一个事件：通知主窗口“我要返回登录页”
        public event Action RequestClose;

        public RegisterViewModel()
        {
            // 监听网络层的注册结果
            ChatClient.Instance.OnRegisterResult += (success, msg) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsBusy = false;
                    StatusMessage = msg;

                    if (success)
                    {
                        // 注册成功，延迟 1.5秒 后自动跳转回登录页
                        System.Threading.Tasks.Task.Delay(1500).ContinueWith(_ =>
                        {
                            Dispatcher.UIThread.InvokeAsync(() => RequestClose?.Invoke());
                        });
                    }
                });
            };
        }

        [RelayCommand]
        private async Task Register() // 改成 async Task
        {
            if (string.IsNullOrWhiteSpace(Account) || string.IsNullOrWhiteSpace(Password))
            {
                StatusMessage = "账号和密码不能为空";
                return;
            }

            IsBusy = true;
            StatusMessage = "正在连接服务器...";

            // 确保先连接服务器 
            // 如果已经连着，它会直接返回 true；如果没连，它会尝试连接
            bool connected = await ChatClient.Instance.ConnectAsync("127.0.0.1", 5555);

            if (!connected)
            {
                StatusMessage = "无法连接到服务器";
                IsBusy = false;
                return;
            }

            StatusMessage = "正在提交注册...";

            // 2. 发送请求
            ChatClient.Instance.Register(Account, Password, Nickname ?? "新用户");
        }

        [RelayCommand]
        private void Back()
        {
            // 手动点击返回按钮
            RequestClose?.Invoke();
        }
    }
}