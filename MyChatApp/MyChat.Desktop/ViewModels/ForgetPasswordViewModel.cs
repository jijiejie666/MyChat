using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyChat.Client.Core;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace MyChat.Desktop.ViewModels
{
    public partial class ForgetPasswordViewModel : ViewModelBase
    {
        [ObservableProperty] private string _account = "";
        [ObservableProperty] private string _nickname = "";
        [ObservableProperty] private string _newPassword = "";
        [ObservableProperty] private string _statusMessage = "请输入账号信息以重置密码";
        [ObservableProperty] private bool _isBusy = false;

        public Action? RequestReturnToLogin;

        public ForgetPasswordViewModel()
        {
            ChatClient.Instance.OnResetPasswordResult += HandleResetResult;
        }

        ~ForgetPasswordViewModel()
        {
            ChatClient.Instance.OnResetPasswordResult -= HandleResetResult;
        }

        private void HandleResetResult(bool success, string msg)
        {
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                IsBusy = false;
                StatusMessage = msg;

                if (success)
                {
                    StatusMessage = "重置成功！正在返回登录页...";
                    await Task.Delay(1500);
                    RequestReturnToLogin?.Invoke();
                }
            });
        }

        // ★★★ 核心修复：把 void 改成 async Task，以便等待连接结果 ★★★
        [RelayCommand]
        private async Task Submit()
        {
            if (string.IsNullOrWhiteSpace(Account) ||
                string.IsNullOrWhiteSpace(Nickname) ||
                string.IsNullOrWhiteSpace(NewPassword))
            {
                StatusMessage = "请填写所有信息";
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "正在连接服务器...";

                // ★★★ 关键一步：先尝试连接服务器 ★★★
                // 如果已经连着，它会直接返回 true；如果没连，它会去连。
                // (假设你的服务器在本地 127.0.0.1:5555)
                bool isConnected = await ChatClient.Instance.ConnectAsync("127.0.0.1", 5555);

                if (!isConnected)
                {
                    IsBusy = false;
                    StatusMessage = "连接服务器失败，请检查网络";
                    return;
                }

                StatusMessage = "正在提交请求...";

                // 确保连接成功后再发送
                ChatClient.Instance.SendResetPassword(Account, Nickname, NewPassword);
            }
            catch (Exception ex)
            {
                IsBusy = false;
                StatusMessage = $"发生错误: {ex.Message}";
            }
        }

        [RelayCommand]
        private void Back()
        {
            RequestReturnToLogin?.Invoke();
        }
    }
}