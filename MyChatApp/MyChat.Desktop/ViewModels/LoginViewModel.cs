using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyChat.Client.Core;
using Avalonia.Threading;

namespace MyChat.Desktop.ViewModels
{
    public partial class LoginViewModel : ViewModelBase
    {
        // ★★★ 新增：服务器 IP 属性 ★★★
        [ObservableProperty]
        private string _serverIp = "127.0.0.1"; // 默认值

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
        private string _account = "";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
        private string _password = "";

        [ObservableProperty]
        private string _statusMessage = "准备就绪";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
        private bool _isBusy = false;

        public Action? LoginSuccess;
        public Action? RequestRegister;
        public Action? RequestForgetPassword;

        public LoginViewModel()
        {
        }

        public void HandleLoginResult(bool success, string msg)
        {
            IsBusy = false;
            if (success)
            {
                StatusMessage = "登录成功，正在跳转...";
                Task.Delay(500).ContinueWith(_ => Dispatcher.UIThread.InvokeAsync(() => LoginSuccess?.Invoke()));
            }
            else
            {
                StatusMessage = $"登录失败: {msg}";
            }
        }

        private bool CanLogin => !string.IsNullOrWhiteSpace(Account) &&
                                 !string.IsNullOrWhiteSpace(Password) &&
                                 !IsBusy;

        [RelayCommand(CanExecute = nameof(CanLogin))]
        private async Task Login()
        {
            if (IsBusy) return;

            if (string.IsNullOrWhiteSpace(Account) || string.IsNullOrWhiteSpace(Password))
            {
                StatusMessage = "请输入账号和密码";
                return;
            }

            // 简单校验 IP
            if (string.IsNullOrWhiteSpace(ServerIp))
            {
                StatusMessage = "请输入服务器IP地址";
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = $"正在连接到 {ServerIp}:5555 ..."; // 提示语也改一下

                bool connected = await Task.Run(() =>
                {
                    try
                    {
                        // ★★★ 关键修复：端口必须和服务端一致 (5555) ★★★
                        return ChatClient.Instance.ConnectAsync(ServerIp, 5555).Result;
                    }
                    catch { return false; }
                });

                if (!connected)
                {
                    StatusMessage = "无法连接服务器，请检查IP/端口/防火墙";
                    IsBusy = false;
                    return;
                }

                StatusMessage = "正在验证身份...";
                ChatClient.Instance.Login(Account, Password);
            }
            catch (Exception ex)
            {
                IsBusy = false;
                StatusMessage = $"错误: {ex.Message}";
            }
        }

        [RelayCommand]
        private void GoToRegister()
        {
            StatusMessage = "";
            RequestRegister?.Invoke();
        }

        [RelayCommand]
        private void GoToForgetPassword()
        {
            RequestForgetPassword?.Invoke();
        }

        public void ResetState()
        {
            IsBusy = false;
            StatusMessage = "请登录";
        }
    }
}