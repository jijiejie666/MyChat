using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MyChat.Desktop.ViewModels;
using MyChat.Desktop.Views; // <--- 必须引用 Views 命名空间

namespace MyChat.Desktop
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            using (var db = new MyChat.Desktop.LocalData.ClientDbContext())
            {
                // ★★★ 加入这一行：强制删除旧数据库 ★★★
                // (运行一次成功后，记得把这行注释掉，否则每次启动聊天记录都没了)
                //db.Database.EnsureDeleted();

                // 创建新数据库 (此时会自动包含新的 Type 字段)
                db.Database.EnsureCreated();
            }
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // 实例化 MainWindow，并绑定主 ViewModel
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel()
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}