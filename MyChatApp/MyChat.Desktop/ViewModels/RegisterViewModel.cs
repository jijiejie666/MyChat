using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyChat.Client.Core;
using System;
using System.Threading.Tasks;

namespace MyChat.Desktop.ViewModels
{
    public partial class RegisterViewModel : ViewModelBase
    {
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
        private string _account = "";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
        private string _password = "";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
        private string _nickname = "";

        [ObservableProperty]
        private string _statusMessage = "";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
        private bool _isBusy = false;

        // 导航事件
        public Action? RequestClose;

        public RegisterViewModel() { }

        private bool CanRegister => !string.IsNullOrWhiteSpace(Account) &&
                                    !string.IsNullOrWhiteSpace(Password) &&
                                    !string.IsNullOrWhiteSpace(Nickname) &&
                                    !IsBusy;

        [RelayCommand(CanExecute = nameof(CanRegister))]
        private async Task Register()
        {
            if (IsBusy) return;

            try
            {
                IsBusy = true; // 锁住按钮
                StatusMessage = "正在连接服务器...";

                // 1. 检查连接
                bool connected = await Task.Run(() =>
                {
                    try { return ChatClient.Instance.ConnectAsync("127.0.0.1", 5555).Result; }
                    catch { return false; }
                });

                if (!connected)
                {
                    StatusMessage = "服务器连接失败";
                    IsBusy = false; // 解锁
                    return;
                }

                StatusMessage = "正在提交注册...";
                // 2. 发送请求 (结果由 MainViewModel 处理)
                ChatClient.Instance.Register(Account, Password, Nickname);

                // 注意：这里不要设置 IsBusy = false，等待 MainViewModel 收到结果后再设置
                // 否则如果网络很快，UI 会闪烁
            }
            catch (Exception ex)
            {
                StatusMessage = $"错误: {ex.Message}";
                IsBusy = false;
            }
        }

        [RelayCommand]
        private void Back()
        {
            StatusMessage = "";
            IsBusy = false; // 强制解锁
            RequestClose?.Invoke();
        }

        public void Reset()
        {
            Account = "";
            Password = "";
            Nickname = "";
            StatusMessage = "";
            IsBusy = false; // ★★★ 关键：重置状态 ★★★
        }
    }
}