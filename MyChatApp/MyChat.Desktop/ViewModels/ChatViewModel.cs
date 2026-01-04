using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyChat.Client.Core;
using MyChat.Desktop.LocalData;
using MyChat.Desktop.Models;
using MyChat.Protocol;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using MyChat.Desktop.Helpers;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace MyChat.Desktop.ViewModels
{
    public partial class ChatViewModel : ViewModelBase
    {
        // ==================== 属性定义 ====================

        // 好友/群组列表
        public ObservableCollection<ChatContact> Contacts { get; } = new();

        // 当前聊天记录
        public ObservableCollection<ChatMessage> Messages { get; } = new();

        // 选中的好友/群
        [ObservableProperty] private ChatContact? _selectedContact;

        // 输入框内容
        [ObservableProperty] private string _inputText = "";

        // 当前登录用户信息
        [ObservableProperty] private string _myNickname;
        [ObservableProperty] private string _myId;
        [ObservableProperty] private string _myAvatarColor;

        // 右侧栏相关
        [ObservableProperty] private ObservableCollection<GroupMemberDto> _currentGroupMembers;
        [ObservableProperty] private bool _showGroupInfo;

        // 导航事件
        public event Action RequestSearch;
        public event Action RequestCreateGroup;
        public event Action RequestLogout;

        // 滚动到底部事件
        public event Action OnNewMessage;

        // ==================== 构造函数 ====================

        public ChatViewModel()
        {
            // 1. 初始化用户信息
            MyNickname = ChatClient.Instance.CurrentNickname ?? "未知用户";
            MyId = ChatClient.Instance.CurrentUserId ?? "Unknown";
            MyAvatarColor = "#5B60F6";

            // 2. 注册所有事件监听
            RegisterEvents();

            // 3. 加载初始数据
            LoadData();
        }

        private void RegisterEvents()
        {
            // 先取消订阅，防止重复 (虽在构造函数里通常没事，但为了安全)
            ChatClient.Instance.OnGetFriendListResult -= UpdateContactList;
            ChatClient.Instance.OnGetGroupListResult -= UpdateGroupList;
            ChatClient.Instance.OnMessageReceived -= OnNetworkMessageReceived;
            ChatClient.Instance.OnFriendStatusChange -= OnFriendStatusChanged;
            ChatClient.Instance.OnGetGroupMembersResult -= UpdateGroupMembers;
            ChatClient.Instance.OnCreateGroupResult -= OnCreateGroupResultHandler;
            ChatClient.Instance.OnFriendRequestReceived -= OnFriendRequestReceived;
            ChatClient.Instance.OnHandleFriendRequestResult -= OnHandleFriendRequestResult;

            // 重新订阅
            ChatClient.Instance.OnGetFriendListResult += UpdateContactList;
            ChatClient.Instance.OnGetGroupListResult += UpdateGroupList;
            ChatClient.Instance.OnMessageReceived += OnNetworkMessageReceived;
            ChatClient.Instance.OnFriendStatusChange += OnFriendStatusChanged;
            ChatClient.Instance.OnGetGroupMembersResult += UpdateGroupMembers;
            ChatClient.Instance.OnCreateGroupResult += OnCreateGroupResultHandler;
            ChatClient.Instance.OnFriendRequestReceived += OnFriendRequestReceived;
            ChatClient.Instance.OnHandleFriendRequestResult += OnHandleFriendRequestResult;
        }

        private void LoadData()
        {
            // 拉取好友和群
            ChatClient.Instance.GetFriendList();
            ChatClient.Instance.GetGroupList();
        }

        // ==================== 事件处理逻辑 ====================

        private void UpdateContactList(List<FriendDto> friendDtos)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                // 保留好友请求项和群组，只刷新普通好友
                var requests = Contacts.Where(c => c.IsRequest).ToList();
                var groups = Contacts.Where(c => c.IsGroup).ToList();

                Contacts.Clear();

                // 1. 先放回好友请求
                foreach (var req in requests) Contacts.Add(req);

                // 2. 加载新好友
                foreach (var f in friendDtos)
                {
                    Contacts.Add(new ChatContact
                    {
                        Id = f.UserId,
                        Name = f.Nickname,
                        AvatarColor = string.IsNullOrEmpty(f.Avatar) ? "#CCCCCC" : f.Avatar,
                        LastMessage = f.IsOnline ? "[在线]" : "[离线]",
                        IsGroup = false,
                        IsRequest = false
                    });
                }

                // 3. 放回群组
                foreach (var g in groups) Contacts.Add(g);
            });
        }

        private void UpdateGroupList(List<GroupDto> groupDtos)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                // 移除旧的群组
                var toRemove = Contacts.Where(c => c.IsGroup).ToList();
                foreach (var r in toRemove) Contacts.Remove(r);

                foreach (var g in groupDtos)
                {
                    Contacts.Insert(0, new ChatContact
                    {
                        Id = g.GroupId,
                        Name = g.GroupName + " (群)",
                        AvatarColor = "#fab1a0",
                        LastMessage = "点击进入群聊",
                        IsGroup = true
                    });
                }
            });
        }

        private void OnFriendRequestReceived(string id, string nickname)
        {
            System.Diagnostics.Debug.WriteLine($"[客户端] 收到好友申请: {nickname} ({id})");
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                // 检查是否已存在
                if (Contacts.Any(c => c.Id == id && c.IsRequest)) return;

                Contacts.Insert(0, new ChatContact
                {
                    Id = id,
                    Name = $"【新朋友】{nickname}",
                    LastMessage = "请求添加你为好友",
                    AvatarColor = "#FF5252",
                    IsRequest = true,
                    IsGroup = false
                });
            });
        }

        private void OnHandleFriendRequestResult(bool success, string msg, string friendId)
        {
            if (success)
            {
                // 刷新好友列表
                ChatClient.Instance.GetFriendList();
            }
        }

        private void OnCreateGroupResultHandler(bool success, string groupId, string name, string msg)
        {
            if (success)
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Contacts.Insert(0, new ChatContact
                    {
                        Id = groupId,
                        Name = name + " (群)",
                        IsGroup = true,
                        AvatarColor = "#FF5252",
                        LastMessage = "群聊已创建"
                    });
                });
            }
        }

        private void OnFriendStatusChanged(string friendId, bool isOnline)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var contact = Contacts.FirstOrDefault(c => c.Id == friendId);
                if (contact != null)
                {
                    contact.LastMessage = isOnline ? "[在线] 刚刚上线" : "[离线]";
                }
            });
        }

        private void UpdateGroupMembers(string groupId, List<GroupMemberDto> members)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var group = Contacts.FirstOrDefault(c => c.Id == groupId);
                if (group == null) return;

                group.Members.Clear();
                int onlineCount = 0;
                foreach (var m in members)
                {
                    group.Members.Add(m);
                    if (m.IsOnline) onlineCount++;
                }
                group.LastMessage = $"成员: {members.Count} 人 ({onlineCount} 在线)";

                if (SelectedContact?.Id == groupId)
                {
                    CurrentGroupMembers = group.Members;
                }
            });
        }

        // ==================== 消息收发逻辑 ====================

        partial void OnSelectedContactChanged(ChatContact? value)
        {
            if (value != null && value.IsRequest)
            {
                _showGroupInfo = false;
                OnPropertyChanged(nameof(ShowGroupInfo));
                return;
            }

            Messages.Clear();
            if (value == null) return;

            // 1. 优先显示内存缓存
            if (value.MessageHistory.Count > 0)
            {
                foreach (var msg in value.MessageHistory) Messages.Add(msg);
                OnNewMessage?.Invoke();
            }
            else
            {
                // 2. 从数据库加载
                LoadHistoryMessages(value);
            }

            // 3. 如果是群，加载成员
            if (value.IsGroup)
            {
                ShowGroupInfo = true;
                CurrentGroupMembers = value.Members;
                if (value.Members.Count == 0)
                {
                    ChatClient.Instance.GetGroupMembers(value.Id);
                }
            }
            else
            {
                ShowGroupInfo = false;
                CurrentGroupMembers = null;
            }
        }

        private void LoadHistoryMessages(ChatContact contact)
        {
            using (var db = new ClientDbContext())
            {
                var myId = ChatClient.Instance.CurrentUserId;
                var targetId = contact.Id;
                List<LocalMessageEntity> history;

                if (contact.IsGroup)
                {
                    history = db.Messages.Where(m => m.ReceiverId == targetId).OrderBy(m => m.TimeTicks).ToList();
                }
                else
                {
                    history = db.Messages.Where(m => (m.SenderId == myId && m.ReceiverId == targetId) || (m.SenderId == targetId && m.ReceiverId == myId))
                                         .OrderBy(m => m.TimeTicks).ToList();
                }

                foreach (var item in history)
                {
                    var type = (MsgType)item.Type;
                    string senderName = "未知";
                    if (item.IsMe) senderName = "我";
                    else if (!string.IsNullOrEmpty(item.SenderName)) senderName = item.SenderName;
                    else senderName = (!contact.IsGroup) ? contact.Name : item.SenderId;

                    var uiMsg = new ChatMessage
                    {
                        Id = item.Id,
                        Content = item.Content,
                        IsMe = item.IsMe,
                        SenderName = senderName,
                        Time = new DateTime(item.TimeTicks),
                        Type = type,
                        ImageContent = (type == MsgType.Image) ? ImageHelper.Base64ToBitmap(item.Content) : null,
                        FileName = item.FileName,
                        FileSizeStr = FileHelper.FormatFileSize(item.FileSize)
                    };

                    contact.MessageHistory.Add(uiMsg);
                    Messages.Add(uiMsg);
                }
            }
            OnNewMessage?.Invoke();
        }

        private void OnNetworkMessageReceived(ChatMsg netMsg)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                string conversationId = netMsg.IsGroup ? netMsg.ReceiverId : netMsg.SenderId;
                var contact = Contacts.FirstOrDefault(c => c.Id == conversationId);
                if (contact == null) return;

                string displaySenderName = netMsg.IsGroup
                    ? (string.IsNullOrEmpty(netMsg.SenderName) ? netMsg.SenderId : netMsg.SenderName)
                    : contact.Name;

                var newMessage = new ChatMessage
                {
                    SenderName = displaySenderName,
                    Content = netMsg.Content,
                    IsMe = false,
                    Time = new DateTime(netMsg.SendTime),
                    Type = netMsg.Type,
                    ImageContent = (netMsg.Type == MsgType.Image) ? ImageHelper.Base64ToBitmap(netMsg.Content) : null,
                    FileName = netMsg.FileName,
                    FileSizeStr = FileHelper.FormatFileSize(netMsg.FileSize)
                };

                contact.MessageHistory.Add(newMessage);

                // 存库
                SaveMessageToLocalDb(new LocalMessageEntity
                {
                    Id = Guid.NewGuid().ToString(),
                    SenderId = netMsg.SenderId,
                    ReceiverId = netMsg.ReceiverId,
                    Content = netMsg.Content,
                    IsMe = false,
                    TimeTicks = DateTime.Now.Ticks,
                    Type = (int)netMsg.Type,
                    SenderName = displaySenderName,
                    SenderAvatar = netMsg.SenderAvatar,
                    FileName = netMsg.FileName,
                    FileSize = netMsg.FileSize
                });

                string preview = (netMsg.Type == MsgType.Image) ? "[图片]" : (netMsg.Type == MsgType.File ? $"[文件] {netMsg.FileName}" : netMsg.Content);
                contact.LastMessage = netMsg.IsGroup ? $"{displaySenderName}: {preview}" : preview;

                if (SelectedContact?.Id == conversationId)
                {
                    Messages.Add(newMessage);
                    OnNewMessage?.Invoke();
                }

                if (netMsg.Type == MsgType.File)
                {
                    FileHelper.SaveBase64ToFile(netMsg.Content, netMsg.FileName);
                }
            });
        }

        private void SendMessageInternal(string content, MsgType type, string fileName = "", long fileSize = 0)
        {
            if (SelectedContact == null) return;

            var newMsg = new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                IsMe = true,
                Content = content,
                Type = type,
                Time = DateTime.Now,
                ImageContent = (type == MsgType.Image) ? ImageHelper.Base64ToBitmap(content) : null,
                FileName = fileName,
                FileSizeStr = FileHelper.FormatFileSize(fileSize)
            };

            SelectedContact.MessageHistory.Add(newMsg);
            Messages.Add(newMsg);
            OnNewMessage?.Invoke();

            SaveMessageToLocalDb(new LocalMessageEntity
            {
                Id = newMsg.Id,
                SenderId = ChatClient.Instance.CurrentUserId,
                ReceiverId = SelectedContact.Id,
                Content = content,
                IsMe = true,
                TimeTicks = newMsg.Time.Ticks,
                Type = (int)type,
                SenderName = MyNickname ?? "我",
                FileName = fileName,
                FileSize = fileSize
            });

            ChatClient.Instance.SendChat(SelectedContact.Id, content, type, SelectedContact.IsGroup, fileName, fileSize);

            string preview = type == MsgType.Image ? "[图片]" : (type == MsgType.File ? $"[文件] {fileName}" : content);
            SelectedContact.LastMessage = SelectedContact.IsGroup ? $"我: {preview}" : preview;
        }

        private void SaveMessageToLocalDb(LocalMessageEntity msg)
        {
            try
            {
                using (var db = new ClientDbContext())
                {
                    db.Messages.Add(msg);
                    db.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"存库失败: {ex.Message}");
            }
        }

        // ==================== 命令绑定 ====================

        [RelayCommand]
        private void Send()
        {
            if (string.IsNullOrWhiteSpace(InputText) || SelectedContact == null) return;
            SendMessageInternal(InputText, MsgType.Text);
            InputText = "";
        }

        [RelayCommand]
        private async Task PickAndSendImage()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var storage = desktop.MainWindow.StorageProvider;
                var result = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "选择图片",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
                });

                if (result != null && result.Count > 0)
                {
                    var path = result[0].Path.LocalPath;
                    string base64 = ImageHelper.FileToBase64(path);
                    if (!string.IsNullOrEmpty(base64))
                    {
                        SendMessageInternal(base64, MsgType.Image);
                    }
                }
            }
        }

        [RelayCommand]
        private async Task PickAndSendFile()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var storage = desktop.MainWindow.StorageProvider;
                var result = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "选择文件", AllowMultiple = false });

                if (result != null && result.Count > 0)
                {
                    var file = result[0];
                    string path = file.Path.LocalPath;
                    var fileInfo = new FileInfo(path);

                    if (fileInfo.Length > 10 * 1024 * 1024) return; // 限制10MB

                    string base64 = FileHelper.FileToBase64(path);
                    SendMessageInternal(base64, MsgType.File, file.Name, fileInfo.Length);
                }
            }
        }

        [RelayCommand]
        private async Task OpenFile(ChatMessage msg)
        {
            if (msg == null || !msg.IsFile || string.IsNullOrEmpty(msg.Content)) return;

            try
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    var file = await desktop.MainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = "另存为",
                        SuggestedFileName = msg.FileName
                    });

                    if (file != null)
                    {
                        var filePath = file.Path.LocalPath;
                        byte[] bytes = Convert.FromBase64String(msg.Content);
                        await File.WriteAllBytesAsync(filePath, bytes);

                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"打开文件失败: {ex.Message}");
            }
        }

        [RelayCommand]
        public void Refresh() => LoadData();

        [RelayCommand]
        private void OpenSearch() => RequestSearch?.Invoke();

        [RelayCommand]
        private void OpenCreateGroup() => RequestCreateGroup?.Invoke();

        [RelayCommand]
        private void Logout()
        {
            ChatClient.Instance.Disconnect();
            RequestLogout?.Invoke();
        }

        [RelayCommand]
        private void AcceptRequest()
        {
            if (SelectedContact != null && SelectedContact.IsRequest)
            {
                ChatClient.Instance.HandleFriendRequest(SelectedContact.Id, true);
                Contacts.Remove(SelectedContact);
                SelectedContact = null;
            }
        }

        [RelayCommand]
        private void RefuseRequest()
        {
            if (SelectedContact != null && SelectedContact.IsRequest)
            {
                ChatClient.Instance.HandleFriendRequest(SelectedContact.Id, false);
                Contacts.Remove(SelectedContact);
                SelectedContact = null;
            }
        }
    }
}