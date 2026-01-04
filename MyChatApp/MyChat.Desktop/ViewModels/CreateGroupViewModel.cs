using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyChat.Client.Core;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.Linq;

namespace MyChat.Desktop.ViewModels
{
    // 辅助类：带勾选框的好友
    public partial class CheckableContact : ObservableObject
    {
        public string Id { get; set; }
        public string Name { get; set; }
        [ObservableProperty] private bool _isChecked;
    }

    public partial class CreateGroupViewModel : ViewModelBase
    {
        [ObservableProperty] private string _groupName;
        [ObservableProperty] private string _statusMessage;
        [ObservableProperty] private bool _isBusy;

        // 好友勾选列表
        public ObservableCollection<CheckableContact> FriendList { get; } = new();

        public System.Action GoBack; // 返回事件

        public CreateGroupViewModel(System.Collections.Generic.List<MyChat.Protocol.FriendDto> friends)
        {
            // 初始化列表
            foreach (var f in friends)
            {
                FriendList.Add(new CheckableContact { Id = f.UserId, Name = f.Nickname });
            }

            // 监听结果
            ChatClient.Instance.OnCreateGroupResult += (success, groupId, name, msg) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsBusy = false;
                    StatusMessage = msg;
                    if (success)
                    {
                        // 成功后延迟1秒返回
                        System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ =>
                             Dispatcher.UIThread.InvokeAsync(() => GoBack?.Invoke()));
                    }
                });
            };
        }

        [RelayCommand]
        private void Create()
        {
            if (string.IsNullOrWhiteSpace(GroupName))
            {
                StatusMessage = "请输入群名称";
                return;
            }

            var selectedIds = FriendList.Where(f => f.IsChecked).Select(f => f.Id).ToList();
            if (selectedIds.Count == 0)
            {
                StatusMessage = "请至少选择一个群成员";
                return;
            }

            IsBusy = true;
            StatusMessage = "正在创建...";
            ChatClient.Instance.CreateGroup(GroupName, selectedIds);
        }

        [RelayCommand]
        private void Back() => GoBack?.Invoke();
    }
}