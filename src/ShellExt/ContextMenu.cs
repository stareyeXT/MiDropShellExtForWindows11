using ShellExtensions;
using ShellExtensions.Helpers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ShellExt
{
    public class ContextMenu : ExplorerCommand
    {
        private readonly string target;
        private readonly string icon;
        private readonly string title;

        public ContextMenu(string target, string icon, string title)
        {
            this.target = target;
            this.icon = icon;
            this.title = title;
        }

        public override string? GetIcon(ShellItemArray shellItems)
        {
            return icon;
        }

        public override string? GetTitle(ShellItemArray shellItems)
        {
            return title;
        }

        public override ExplorerCommandState GetState(ShellItemArray shellItems, bool fOkToBeSlow, out bool pending)
        {
            pending = false;
            var accessorProbe = this.ServiceProvider.GetService<ShellExtensions.IContextMenuTypeAccessor>();

            if (accessorProbe is { } accessor)
            {
                if (accessor.ContextMenuType == ContextMenuType.ModernContextMenu
                    || accessor.ContextMenuType == ContextMenuType.Unknown)
                {
                    return ExplorerCommandState.ECS_ENABLED;
                }
            }

            // 小米这条路：拿不到 site / accessor 时必须照常显示。
            // 实测：HKCU 裸注册时 Explorer 在经典菜单里不给 accessor，
            // 上游这段就会返回 ECS_HIDDEN，整项从右键菜单里消失（16:49 复现）。
            // 华为/荣耀仍旧走上游原逻辑，不受影响。
            if (string.Equals(target, "XiaomiShare", StringComparison.Ordinal))
            {
                return ExplorerCommandState.ECS_ENABLED;
            }

            return ExplorerCommandState.ECS_HIDDEN;
        }

        public override unsafe void Invoke(ExplorerCommandInvokeEventArgs args)
        {
            var files = args.ShellItems.Select(c => c.FullPath).ToArray();

            // 只有小米互传这条会落盘诊断日志并把异常吞在扩展内部；
            // 华为/荣耀保持原来的单行调用，一个字节都不变。
            if (string.Equals(target, "XiaomiShare", StringComparison.Ordinal))
            {
                try
                {
                    if (files != null && files.Length > 0)
                    {
                        DllMain.StartShare(target, files);
                    }
                }
                catch (Exception ex)
                {
                    DllMain.Trace("Invoke threw: " + ex);
                }
                return;
            }

            if (files != null && files.Length > 0)
            {
                DllMain.StartShare(target, files);
            }
        }

    }
}
