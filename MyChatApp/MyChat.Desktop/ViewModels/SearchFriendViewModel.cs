using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyChat.Client.Core;
using Avalonia.Threading;
using System.Threading.Tasks;
using System;
using MyChat.Protocol; // ★★★ 必须引用，用于识别 FriendDto ★★★

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

        // 析构时取消订阅
        ~SearchFriendViewModel()
        {
            ChatClient.Instance.OnSearchUserResult -= OnSearchResult;
            ChatClient.Instance.OnAddFriendResult -= OnAddResult;
        }

        // ★★★ 修复 1：参数改为 (bool success, FriendDto? user) ★★★
        private void OnSearchResult(bool success, FriendDto? user)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsBusy = false;
                if (success && user != null)
                {
                    StatusMessage = "";
                    HasResult = true;
                    FoundId = user.UserId;
                    FoundName = user.Nickname;
                    // FriendDto 里没有 Account 字段，这里我们暂时用 SearchText (用户输入的账号) 或者 ID 代替
                    FoundAccount = SearchText;
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

            // ★★★ 修复 2：AddFriend 现在只需要对方 ID，不需要传 MyId ★★★
            ChatClient.Instance.AddFriend(FoundId);
        }

        [RelayCommand]
        private void Back()
        {
            // 清理事件监听
            ChatClient.Instance.OnSearchUserResult -= OnSearchResult;
            ChatClient.Instance.OnAddFriendResult -= OnAddResult;

            GoBack?.Invoke();
        }
    }
}