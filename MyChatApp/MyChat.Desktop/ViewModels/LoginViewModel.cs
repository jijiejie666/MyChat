using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyChat.Client.Core;
using Avalonia.Threading;
using System.Security.Principal;

namespace MyChat.Desktop.ViewModels
{
    public partial class LoginViewModel : ViewModelBase
    {
        // 统一使用 Account，与服务端协议保持一致
        [ObservableProperty] private string _account = "admin";
        [ObservableProperty] private string _password = "123";
        [ObservableProperty] private string _statusMessage = "准备就绪";
        [ObservableProperty] private bool _isBusy = false;

        // 定义导航事件
        public Action? LoginSuccess;          // 登录成功
        public Action? RequestRegister;       // 跳转注册
        public Action? RequestForgetPassword; // 跳转忘记密码

        public LoginViewModel()
        {
            // 监听客户端的登录结果回调
            ChatClient.Instance.OnLoginResult += (success, msg) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsBusy = false;
                    if (success)
                    {
                        StatusMessage = $"登录成功! {msg}";
                        // 延迟一小会儿跳转，让用户看到成功提示
                        Task.Delay(500).ContinueWith(_ =>
                        {
                            Dispatcher.UIThread.InvokeAsync(() => LoginSuccess?.Invoke());
                        });
                    }
                    else
                    {
                        StatusMessage = $"登录失败: {msg}";
                    }
                });
            };
        }

        [RelayCommand]
        private async Task Login()
        {
            if (IsBusy) return;

            if (string.IsNullOrWhiteSpace(Account) || string.IsNullOrWhiteSpace(Password))
            {
                StatusMessage = "请输入账号和密码";
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "正在连接服务器...";

                // ★★★ 关键修改：先强制断开旧连接，确保状态干净 ★★★
                ChatClient.Instance.Disconnect();

                // 重新建立连接
                bool connected = await ChatClient.Instance.ConnectAsync("127.0.0.1", 5555);

                if (!connected)
                {
                    StatusMessage = "服务器连接失败，请检查服务端是否开启。";
                    IsBusy = false;
                    return;
                }

                StatusMessage = "正在验证身份...";
                ChatClient.Instance.Login(Account, Password);
            }
            catch (Exception ex)
            {
                IsBusy = false;
                StatusMessage = $"发生错误: {ex.Message}";
            }
        }

        [RelayCommand]
        private void GoToRegister()
        {
            RequestRegister?.Invoke();
        }

        [RelayCommand]
        private void GoToForgetPassword()
        {
            RequestForgetPassword?.Invoke();
        }
    }
}