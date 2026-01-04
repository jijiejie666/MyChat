using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic; // 引用 List

namespace MyChat.Desktop.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        // 当前显示的页面 (LoginVM 或 RegisterVM 或 ChatVM)
        [ObservableProperty]
        private ViewModelBase _currentContent;

        public MainWindowViewModel()
        {
            // 启动时显示登录页
            ShowLogin();
        }

        // --- 显示登录页 (核心导航逻辑) ---
        public void ShowLogin()
        {
            var loginVm = new LoginViewModel();

            // 1. 处理注册请求 -> 跳转到注册页
            loginVm.RequestRegister += ShowRegister;

            // 2. 处理忘记密码请求 -> 跳转到忘记密码页
            loginVm.RequestForgetPassword += () =>
            {
                var forgetVm = new ForgetPasswordViewModel();
                // 点击返回时，重新显示登录页
                forgetVm.GoBack += () => ShowLogin();
                CurrentContent = forgetVm;
            };

            // 3. 处理登录成功 -> 跳转到聊天页
            loginVm.LoginSuccess += () =>
            {
                // 创建聊天 ViewModel
                var chatVm = new ChatViewModel();

                // --- 配置聊天页面的子导航事件 ---

                // A. 处理注销 -> 回到登录页
                chatVm.RequestLogout += () =>
                {
                    ShowLogin();
                };

                // B. 处理搜索好友 -> 跳转搜索页
                chatVm.RequestSearch += () =>
                {
                    var searchVm = new SearchFriendViewModel();

                    // 当搜索页返回时，切回 chatVm
                    searchVm.GoBack += () =>
                    {
                        CurrentContent = chatVm;
                        // 刷新好友列表 (确保 ChatViewModel 有 RefreshCommand)
                        chatVm.RefreshCommand.Execute(null);
                    };

                    CurrentContent = searchVm;
                };

                // C. 处理建群 -> 跳转建群页
                chatVm.RequestCreateGroup += () =>
                {
                    // 筛选出纯好友（非群组）传给建群页面
                    var friends = chatVm.Contacts
                        .Where(c => !c.IsGroup)
                        .Select(c => new MyChat.Protocol.FriendDto { UserId = c.Id, Nickname = c.Name })
                        .ToList();

                    var createVm = new CreateGroupViewModel(friends);

                    // 建群完成或取消后，返回聊天页
                    createVm.GoBack += () => CurrentContent = chatVm;

                    CurrentContent = createVm;
                };

                // 切换到聊天主页
                CurrentContent = chatVm;
            };

            // 初始显示登录页
            CurrentContent = loginVm;
        }

        // --- 显示注册页 ---
        public void ShowRegister()
        {
            var regVm = new RegisterViewModel();

            // 注册成功或点击返回 -> 回登录页
            regVm.RequestClose += ShowLogin;

            CurrentContent = regVm;
        }
    }
}