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
using Avalonia.Media.Imaging;
using Avalonia.Data.Converters;

namespace MyChat.Desktop.ViewModels
{
    public partial class ChatViewModel : ViewModelBase
    {
        // 静态转换器
        public static FuncValueConverter<string, Bitmap?> Base64ToBitmapConverter { get; } =
            new FuncValueConverter<string, Bitmap?>(base64 => ImageHelper.Base64ToBitmap(base64));

        // ==================== 属性定义 ====================

        public ObservableCollection<ChatContact> Contacts { get; } = new();
        public ObservableCollection<ChatMessage> Messages { get; } = new();

        [ObservableProperty] private ChatContact? _selectedContact;
        [ObservableProperty] private string _inputText = "";

        // 当前登录用户信息
        [ObservableProperty] private string _myNickname;
        [ObservableProperty] private string _myId;
        [ObservableProperty] private string _myAvatarColor;
        [ObservableProperty] private Bitmap? _myAvatar;

        [ObservableProperty] private ObservableCollection<GroupMemberDto> _currentGroupMembers;
        [ObservableProperty] private bool _showGroupInfo;

        public event Action RequestSearch;
        public event Action RequestCreateGroup;
        public event Action RequestLogout;
        public event Action OnNewMessage;

        public bool IsAdmin => _myId == "10000" || (_myNickname != null && _myNickname.ToLower().Contains("superadmin"));
        [ObservableProperty] private string _broadcastText = "";

        public int AdminOnlineCount => Contacts.Count(c => !c.IsGroup && !c.IsRequest && c.LastMessage.Contains("在线"));
        public int AdminMessageCount => Messages.Count;
        public string ServerStatusText => "🟢 运行正常";

        // ==================== 构造函数 ====================

        public ChatViewModel()
        {
            MyNickname = ChatClient.Instance.CurrentNickname ?? "未知用户";
            MyId = ChatClient.Instance.CurrentUserId ?? "Unknown";
            MyAvatarColor = "#5B60F6";

            LoadMyAvatar();
            RegisterEvents();

            ChatClient.Instance.OnUpdateUserInfoResult += OnUpdateUserInfo;
            ChatClient.Instance.OnFriendInfoUpdate += OnFriendInfoUpdateHandler;

            LoadData();
        }

        ~ChatViewModel()
        {
            ChatClient.Instance.OnUpdateUserInfoResult -= OnUpdateUserInfo;
            ChatClient.Instance.OnFriendInfoUpdate -= OnFriendInfoUpdateHandler;
        }

        private void RegisterEvents()
        {
            ChatClient.Instance.OnGetFriendListResult -= UpdateContactList;
            ChatClient.Instance.OnGetGroupListResult -= UpdateGroupList;
            ChatClient.Instance.OnMessageReceived -= OnNetworkMessageReceived;
            ChatClient.Instance.OnFriendStatusChange -= OnFriendStatusChanged;
            ChatClient.Instance.OnGetGroupMembersResult -= UpdateGroupMembers;
            ChatClient.Instance.OnCreateGroupResult -= OnCreateGroupResultHandler;
            ChatClient.Instance.OnFriendRequestReceived -= OnFriendRequestReceived;
            ChatClient.Instance.OnHandleFriendRequestResult -= OnHandleFriendRequestResult;
            ChatClient.Instance.OnGroupInvitationReceived -= OnGroupInvitationReceivedHandler;

            ChatClient.Instance.OnGetFriendListResult += UpdateContactList;
            ChatClient.Instance.OnGetGroupListResult += UpdateGroupList;
            ChatClient.Instance.OnMessageReceived += OnNetworkMessageReceived;
            ChatClient.Instance.OnFriendStatusChange += OnFriendStatusChanged;
            ChatClient.Instance.OnGetGroupMembersResult += UpdateGroupMembers;
            ChatClient.Instance.OnCreateGroupResult += OnCreateGroupResultHandler;
            ChatClient.Instance.OnFriendRequestReceived += OnFriendRequestReceived;
            ChatClient.Instance.OnHandleFriendRequestResult += OnHandleFriendRequestResult;
            ChatClient.Instance.OnGroupInvitationReceived += OnGroupInvitationReceivedHandler;
        }

        private void LoadData()
        {
            ChatClient.Instance.GetFriendList();
            ChatClient.Instance.GetGroupList();
        }

        private void LoadMyAvatar()
        {
            string base64 = ChatClient.Instance.CurrentUserAvatar;
            MyAvatar = ImageHelper.Base64ToBitmap(base64);
        }

        private void OnUpdateUserInfo(bool success, string msg, string newNick, string newAvatar)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (success)
                {
                    if (!string.IsNullOrEmpty(newNick)) MyNickname = newNick;
                    if (!string.IsNullOrEmpty(newAvatar)) MyAvatar = ImageHelper.Base64ToBitmap(newAvatar);
                }
            });
        }

        private void OnFriendInfoUpdateHandler(string friendId, string newNick, string newAvatar)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var contact = Contacts.FirstOrDefault(c => c.Id == friendId);
                if (contact != null)
                {
                    if (!string.IsNullOrEmpty(newNick)) contact.Name = newNick;
                    if (!string.IsNullOrEmpty(newAvatar)) contact.AvatarBase64 = newAvatar;
                    if (SelectedContact?.Id == friendId) OnPropertyChanged(nameof(SelectedContact));
                }
            });
        }

        // ==================== 核心逻辑 ====================

        private void LoadHistoryMessages(ChatContact contact)
        {
            using (var db = new ClientDbContext())
            {
                var myId = ChatClient.Instance.CurrentUserId;
                var targetId = contact.Id;
                List<LocalMessageEntity> history;

                if (contact.IsGroup)
                {
                    history = db.Messages.Where(m => m.ReceiverId == targetId)
                                       .OrderBy(m => m.TimeTicks)
                                       .ToList();
                }
                else
                {
                    history = db.Messages.Where(m => (m.SenderId == myId && m.ReceiverId == targetId) ||
                                                     (m.SenderId == targetId && m.ReceiverId == myId))
                                       .OrderBy(m => m.TimeTicks)
                                       .ToList();
                }

                foreach (var item in history)
                {
                    var type = (MsgType)item.Type;
                    bool isActuallyMe = (item.SenderId == myId);

                    string senderName = "未知";
                    Bitmap? avatarImg = null;

                    if (isActuallyMe)
                    {
                        senderName = "我";
                        avatarImg = MyAvatar;
                    }
                    else
                    {
                        if (!contact.IsGroup)
                        {
                            senderName = contact.Name;
                            avatarImg = contact.AvatarBitmap;
                        }
                        else
                        {
                            senderName = !string.IsNullOrEmpty(item.SenderName) ? item.SenderName : item.SenderId;
                            var mem = contact.Members.FirstOrDefault(m => m.UserId == item.SenderId);
                            if (mem != null && !string.IsNullOrEmpty(mem.Avatar))
                            {
                                avatarImg = ImageHelper.Base64ToBitmap(mem.Avatar);
                            }
                        }
                    }

                    var uiMsg = new ChatMessage
                    {
                        Id = item.Id,
                        SenderId = item.SenderId,
                        Content = item.Content,
                        IsMe = isActuallyMe,
                        SenderName = senderName,
                        Time = new DateTime(item.TimeTicks),
                        Type = type,
                        ImageContent = (type == MsgType.Image) ? ImageHelper.Base64ToBitmap(item.Content) : null,
                        FileName = item.FileName,
                        FileSizeStr = FileHelper.FormatFileSize(item.FileSize),
                        SenderAvatarBitmap = avatarImg
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
                // 系统广播处理
                if (netMsg.SenderId == "SYSTEM")
                {
                    var sysContact = Contacts.FirstOrDefault(c => c.Id == "SYSTEM");
                    if (sysContact == null)
                    {
                        sysContact = new ChatContact
                        {
                            Id = "SYSTEM",
                            Name = "📢 系统广播",
                            AvatarColor = "#FF5252",
                            IsOnline = true,
                            LastMessage = "系统通知",
                            IsGroup = false,
                            IsRequest = false
                        };
                        Contacts.Insert(0, sysContact);
                    }
                }

                string conversationId = netMsg.IsGroup ? netMsg.ReceiverId : netMsg.SenderId;
                var contact = Contacts.FirstOrDefault(c => c.Id == conversationId);

                if (contact == null) return;

                // 查找头像
                Bitmap? senderAvatarImg = null;
                if (netMsg.IsGroup)
                {
                    var member = contact.Members.FirstOrDefault(m => m.UserId == netMsg.SenderId);
                    if (member != null && !string.IsNullOrEmpty(member.Avatar))
                    {
                        senderAvatarImg = ImageHelper.Base64ToBitmap(member.Avatar);
                    }
                }
                else
                {
                    senderAvatarImg = contact.AvatarBitmap;
                }

                // ★★★ 核心修复：AI流式消息特殊处理 ★★★
                if (netMsg.Type == MsgType.Aistream)
                {
                    // 尝试找到已经存在的这条消息
                    var existingMsg = contact.MessageHistory.FirstOrDefault(m => m.Id == netMsg.Id);

                    if (existingMsg != null)
                    {
                        // 如果找到了，直接追加内容
                        // 因为 ChatMessage.Content 已经是 ObservableProperty，界面会自动刷新
                        existingMsg.Content += netMsg.Content;
                        contact.LastMessage = "AI 正在输入...";
                    }
                    else
                    {
                        // 如果是第一个字，创建新消息
                        var newMsg = new ChatMessage
                        {
                            Id = netMsg.Id,
                            SenderId = netMsg.SenderId,
                            SenderName = netMsg.SenderName,
                            Content = netMsg.Content, // 第一个字
                            IsMe = false,
                            Time = new DateTime(netMsg.SendTime),
                            Type = MsgType.Text, // 界面上当作 Text 显示
                            SenderAvatarBitmap = senderAvatarImg
                        };

                        contact.MessageHistory.Add(newMsg);

                        // 如果当前正好选中了这个会话，也要加到 Messages 集合里显示出来
                        if (SelectedContact?.Id == conversationId)
                        {
                            Messages.Add(newMsg);
                            OnNewMessage?.Invoke(); // 滚到底部
                        }
                    }
                    return; // 流式消息处理完毕，直接返回，不存库（直到最后一条完整的才存）
                }

                // 普通消息处理 (Text, Image, File 等)
                string displaySenderName = netMsg.IsGroup
                    ? (string.IsNullOrEmpty(netMsg.SenderName) ? netMsg.SenderId : netMsg.SenderName)
                    : contact.Name;

                var newMessage = new ChatMessage
                {
                    Id = netMsg.Id,
                    SenderId = netMsg.SenderId,
                    SenderName = displaySenderName,
                    Content = netMsg.Content,
                    IsMe = false,
                    Time = new DateTime(netMsg.SendTime),
                    Type = netMsg.Type,
                    ImageContent = (netMsg.Type == MsgType.Image) ? ImageHelper.Base64ToBitmap(netMsg.Content) : null,
                    FileName = netMsg.FileName,
                    FileSizeStr = FileHelper.FormatFileSize(netMsg.FileSize),
                    SenderAvatarBitmap = senderAvatarImg
                };

                contact.MessageHistory.Add(newMessage);

                // 存库 (流式消息中间态不存库，只有非流式消息存库)
                if (netMsg.Type != MsgType.Aistream)
                {
                    SaveMessageToLocalDb(new LocalMessageEntity
                    {
                        Id = netMsg.Id,
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
                }

                string preview = (netMsg.Type == MsgType.Image) ? "[图片]" : (netMsg.Type == MsgType.File ? $"[文件] {netMsg.FileName}" : netMsg.Content);
                contact.LastMessage = netMsg.IsGroup ? $"{displaySenderName}: {preview}" : preview;

                if (SelectedContact?.Id == conversationId)
                {
                    Messages.Add(newMessage);
                    OnNewMessage?.Invoke();
                    UpdateAdminStats();
                }

                if (netMsg.Type == MsgType.File)
                {
                    FileHelper.SaveBase64ToFile(netMsg.Content, netMsg.FileName);
                }
            });
        }

        // ==================== 其他方法 ====================

        private void UpdateContactList(List<FriendDto> friendDtos)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var requests = Contacts.Where(c => c.IsRequest).ToList();
                var groups = Contacts.Where(c => c.IsGroup).ToList();

                Contacts.Clear();

                foreach (var req in requests) Contacts.Add(req);

                foreach (var f in friendDtos)
                {
                    Contacts.Add(new ChatContact
                    {
                        Id = f.UserId,
                        Name = f.Nickname,
                        AvatarBase64 = f.Avatar,
                        AvatarColor = "#CCCCCC",
                        LastMessage = f.IsOnline ? "[在线] 刚刚上线" : "[离线]",
                        IsGroup = false,
                        IsRequest = false
                    });
                }

                foreach (var g in groups) Contacts.Add(g);
                UpdateAdminStats();
            });
        }

        private void UpdateGroupList(List<GroupDto> groupDtos)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
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
            Dispatcher.UIThread.InvokeAsync(() =>
            {
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
            if (success) ChatClient.Instance.GetFriendList();
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

        private void OnGroupInvitationReceivedHandler(string groupId, string groupName, string ownerId)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Contacts.Any(c => c.Id == groupId)) return;
                Contacts.Insert(0, new ChatContact
                {
                    Id = groupId,
                    Name = groupName + " (群)",
                    IsGroup = true,
                    AvatarColor = "#fab1a0",
                    LastMessage = "你已被邀请加入群聊",
                    IsOnline = true,
                    IsRequest = false
                });
            });
        }

        private void OnFriendStatusChanged(string friendId, bool isOnline)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var contact = Contacts.FirstOrDefault(c => c.Id == friendId);
                if (contact != null)
                {
                    contact.LastMessage = isOnline ? "[在线] 刚刚上线" : "[离线]";
                    UpdateAdminStats();
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
                if (SelectedContact?.Id == groupId) CurrentGroupMembers = group.Members;
            });
        }

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

            if (value.MessageHistory.Count > 0)
            {
                foreach (var msg in value.MessageHistory) Messages.Add(msg);
                OnNewMessage?.Invoke();
            }
            else
            {
                LoadHistoryMessages(value);
            }

            if (value.IsGroup)
            {
                ShowGroupInfo = true;
                CurrentGroupMembers = value.Members;
                if (value.Members.Count == 0) ChatClient.Instance.GetGroupMembers(value.Id);
            }
            else
            {
                ShowGroupInfo = false;
                CurrentGroupMembers = null;
            }
            UpdateAdminStats();
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
            UpdateAdminStats();

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

        public void UpdateAdminStats()
        {
            OnPropertyChanged(nameof(AdminOnlineCount));
            OnPropertyChanged(nameof(AdminMessageCount));
            OnPropertyChanged(nameof(ServerStatusText));
        }

        [RelayCommand]
        private async Task ChangeAvatar()
        {
            try
            {
                var lifetime = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                var mainWindow = lifetime?.MainWindow;
                if (mainWindow == null) return;

                var files = await mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "选择头像",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
                });

                if (files.Count >= 1)
                {
                    var file = files[0];
                    string filePath = file.Path.LocalPath;
                    string base64 = ImageHelper.FileToBase64(filePath);
                    if (!string.IsNullOrEmpty(base64)) ChatClient.Instance.UpdateUserInfo(MyNickname, base64);
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"更换头像出错: {ex.Message}"); }
        }

        [RelayCommand]
        private void SendBroadcast()
        {
            if (string.IsNullOrWhiteSpace(BroadcastText)) return;
            ChatClient.Instance.SendChat("SERVER", $"/broadcast {BroadcastText}", MsgType.Text, false);
            BroadcastText = "";
        }

        [RelayCommand]
        private void KickUser(object parameter)
        {
            if (parameter is ChatContact user && user.Id != MyId)
            {
                ChatClient.Instance.SendChat("SERVER", $"/kick {user.Id}", MsgType.Text, false);
            }
        }

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
                    if (!string.IsNullOrEmpty(base64)) SendMessageInternal(base64, MsgType.Image);
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
                    if (fileInfo.Length > 10 * 1024 * 1024) return;
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
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"打开文件失败: {ex.Message}"); }
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