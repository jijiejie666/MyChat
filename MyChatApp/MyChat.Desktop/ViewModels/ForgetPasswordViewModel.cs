using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyChat.Client.Core;
using Avalonia.Threading;
using System;

namespace MyChat.Desktop.ViewModels
{
    public partial class ForgetPasswordViewModel : ViewModelBase
    {
        [ObservableProperty] private string _account;
        [ObservableProperty] private string _nickname;
        [ObservableProperty] private string _newPassword;
        [ObservableProperty] private string _statusMessage;
        [ObservableProperty] private bool _isBusy;

        public Action GoBack { get; set; } // 返回登录页的委托

        public ForgetPasswordViewModel()
        {
            ChatClient.Instance.OnResetPasswordResult += (success, msg) =>
            {
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    IsBusy = false;
                    StatusMessage = msg;
                    if (success)
                    {
                        // 成功后延迟 1.5秒 返回登录页
                        await System.Threading.Tasks.Task.Delay(1500);
                        GoBack?.Invoke();
                    }
                });
            };
        }

        [RelayCommand]
        private void Submit()
        {
            if (string.IsNullOrEmpty(Account) || string.IsNullOrEmpty(Nickname) || string.IsNullOrEmpty(NewPassword))
            {
                StatusMessage = "请填写所有信息";
                return;
            }

            IsBusy = true;
            StatusMessage = "正在提交请求...";
            ChatClient.Instance.Connect("127.0.0.1", 8888); // 确保连接
            ChatClient.Instance.ResetPassword(Account, Nickname, NewPassword);
        }

        [RelayCommand]
        private void Back()
        {
            GoBack?.Invoke();
        }
    }
}