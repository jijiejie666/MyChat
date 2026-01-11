using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using MyChat.Protocol;
using MyChat.Client.Core;
using Avalonia.Threading;

namespace MyChat.Desktop.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        [ObservableProperty]
        private ViewModelBase _currentContent;

        // 保持单例，避免状态丢失
        private readonly LoginViewModel _loginVm;
        private readonly RegisterViewModel _registerVm;

        public MainWindowViewModel()
        {
            // 1. 初始化所有 ViewModel
            _loginVm = new LoginViewModel();
            _registerVm = new RegisterViewModel();

            // 2. 绑定 Login 页面事件
            _loginVm.RequestRegister += ShowRegister;
            _loginVm.LoginSuccess += OnLoginSuccess;

            // ★★★ 新增：绑定忘记密码事件 ★★★
            _loginVm.RequestForgetPassword += ShowForgetPassword;

            // 3. 绑定 Register 页面事件
            _registerVm.RequestClose += ShowLogin;

            // 4. 全局监听网络事件 (最关键的部分)
            ChatClient.Instance.OnLoginResult += OnLoginResultHandler;
            ChatClient.Instance.OnRegisterResult += OnRegisterResultHandler;

            // 5. 默认显示登录
            _currentContent = _loginVm;
        }

        // --- 网络回调处理 ---

        private void OnLoginResultHandler(bool success, string msg)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                // 交给 LoginVM 处理 UI 变化 (变亮按钮、显示错误)
                _loginVm.HandleLoginResult(success, msg);
            });
        }

        private void OnRegisterResultHandler(bool success, string msg)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                // 收到结果无论成功失败，先解锁注册页
                _registerVm.IsBusy = false;

                if (success)
                {
                    // 注册成功 -> 自动切回登录页
                    ShowLogin();

                    // 自动填入账号
                    _loginVm.Account = _registerVm.Account;
                    _loginVm.Password = ""; // 密码留空
                    _loginVm.StatusMessage = "注册成功，请登录";
                }
                else
                {
                    // 注册失败 -> 停在注册页显示错误
                    _registerVm.StatusMessage = msg;
                }
            });
        }

        // --- 导航逻辑 ---

        public void ShowLogin()
        {
            // 每次切回登录页，强制重置状态
            _loginVm.ResetState();
            CurrentContent = _loginVm;
        }

        public void ShowRegister()
        {
            _registerVm.Reset(); // 每次进入注册页都清空
            CurrentContent = _registerVm;
        }

        // ★★★ 新增：显示忘记密码页面 ★★★
        public void ShowForgetPassword()
        {
            var forgetVm = new ForgetPasswordViewModel();
            // 绑定返回事件
            forgetVm.RequestReturnToLogin += ShowLogin;
            CurrentContent = forgetVm;
        }

        private void OnLoginSuccess()
        {
            // 创建聊天页面 (ChatVM 需要每次重新创建以加载最新数据)
            var chatVm = new ChatViewModel();

            // 绑定聊天页面的子事件
            chatVm.RequestLogout += ShowLogin;

            chatVm.RequestSearch += () =>
            {
                var searchVm = new SearchFriendViewModel();
                searchVm.GoBack += () => { CurrentContent = chatVm; chatVm.RefreshCommand.Execute(null); };
                CurrentContent = searchVm;
            };

            chatVm.RequestCreateGroup += () =>
            {
                var friends = chatVm.Contacts.Where(c => !c.IsGroup).Select(c => new FriendDto { UserId = c.Id, Nickname = c.Name }).ToList();
                var createVm = new CreateGroupViewModel(friends);
                createVm.GoBack += () => CurrentContent = chatVm;
                CurrentContent = createVm;
            };

            CurrentContent = chatVm;
        }
    }
}