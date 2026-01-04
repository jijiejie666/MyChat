using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyChat.Client.Core;
using Avalonia.Threading;
using System.Threading.Tasks;
using System;

namespace MyChat.Desktop.ViewModels
{
    public partial class SearchFriendViewModel : ViewModelBase
    {
        [ObservableProperty] private string _searchText;
        [ObservableProperty] private string _statusMessage;
        [ObservableProperty] private bool _isBusy;

        // 搜索结果展示
        [ObservableProperty] private bool _hasResult;
        [ObservableProperty] private string _foundName;
        [ObservableProperty] private string _foundAccount;
        [ObservableProperty] private string _foundId;

        // 定义返回事件
        public Action GoBack;

        public SearchFriendViewModel()
        {
            // 监听搜索结果
            ChatClient.Instance.OnSearchUserResult += OnSearchResult;

            // 监听添加结果
            ChatClient.Instance.OnAddFriendResult += OnAddResult;
        }

        // 为了防止内存泄漏，如果是长期存在的对象，最好提供 Unsubscribe 方法
        // 但对于这种一次性的页面 ViewModel，通常不用太担心，这里用命名方法是为了代码整洁
        private void OnSearchResult(bool success, string userId, string nickname, string account)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsBusy = false;
                if (success)
                {
                    StatusMessage = "";
                    HasResult = true;
                    FoundId = userId;
                    FoundName = nickname;
                    FoundAccount = account;
                }
                else
                {
                    HasResult = false;
                    StatusMessage = "未找到该用户";
                }
            });
        }

        private void OnAddResult(bool success, string msg)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsBusy = false;
                // ★★★ 优化点：直接显示服务器返回的消息（例如 "好友申请已发送..."）
                // 这样用户就知道只是发了申请，而不是已经加上了
                StatusMessage = success ? msg : $"操作失败: {msg}";
            });
        }

        [RelayCommand]
        private void Search()
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return;

            IsBusy = true;
            HasResult = false; // 重置上次结果
            StatusMessage = "正在查找...";

            // 发起搜索
            ChatClient.Instance.SearchUser(SearchText);
        }

        [RelayCommand]
        private void AddFriend()
        {
            if (!HasResult) return;

            IsBusy = true;
            StatusMessage = "正在发送请求...";

            string myId = ChatClient.Instance.CurrentUserId;

            if (string.IsNullOrEmpty(myId))
            {
                StatusMessage = "错误：未获取到登录信息";
                IsBusy = false;
                return;
            }

            // 发送添加请求
            ChatClient.Instance.AddFriend(myId, FoundId);
        }

        [RelayCommand]
        private void Back()
        {
            // 简单清理一下事件监听，防止多次回调（虽然 new 出来的一般没事，但好习惯）
            ChatClient.Instance.OnSearchUserResult -= OnSearchResult;
            ChatClient.Instance.OnAddFriendResult -= OnAddResult;

            GoBack?.Invoke();
        }
    }
}