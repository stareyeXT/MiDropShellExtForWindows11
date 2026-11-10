MiDropShellExt —— 给 Windows 11 加上「使用小米互传发送」
适配小米电脑管家 6.0.2.30（本分支）

安装
  1. 解压到一个固定目录（以后卸载要用同一个目录）
  2. 右键「安装.ps1」→ 使用 PowerShell 运行
     脚本会自己请求提权（UAC 点「是」），然后：
       · 把 helper 放到 C:\Program Files\MI\MiDrop.Helper\MiDrop.Helper.exe
       · 把 DLL、helper 副本、XiaomiImages\、AppxManifest.xml 放到 %LOCALAPPDATA%\MiDropHelper\
       · 注册 sparse 包 MiDropShellExt.Sparse
     拒绝 UAC（点「否」）：脚本会以 -NoElevate 继续，只装 %LOCALAPPDATA% 那份，
     四个入口功能不受影响，只是 helper 不在 Program Files。
  3. 装完脚本会问要不要重启资源管理器，选 Y

前置条件
  Win11（Build ≥ 22621）+ 已开启开发者模式（设置 → 系统 → 开发者选项）
  不需要装 .NET，二进制是自包含 AOT

装了什么
  C:\Program Files\MI\MiDrop.Helper\MiDrop.Helper.exe   真正执行发送的进程（提权成功时）
  %LOCALAPPDATA%\MiDropHelper\                          菜单处理器 DLL + 清单需要的图片和副本
  一个 sparse 包 MiDropShellExt.Sparse                  新式菜单 / 共享面板 / 开始菜单磁贴

怎么用
  新式菜单  → 右键文件 →「应用扩展」→「使用小米互传发送」
  经典菜单  → 右键文件 →「显示更多选项」→「使用小米互传发送」（可能有 2 条，正常）
  共享面板  → 右键文件 →「共享」→ 列表里的「使用小米互传发送」
  开始菜单  → 应用列表里的「使用小米互传发送」（打开管家的「互联」页）
  点击只弹「小米互传 · 发送」窗口，不点设备不会真的发送。

卸载
  运行同一个包里的「卸载.ps1」（同样会自己请求提权）。
  它会注销 sparse 包、删掉 %LOCALAPPDATA%\MiDropHelper；
  删 C:\Program Files\MI\MiDrop.Helper 前需要输入大写 YES 二次确认。

更多文档
  安装细节、错误码、回滚步骤 见 docs\安装说明.md、docs\回滚说明.md
  功能逐条对照             见 docs\功能对照表.md

已知问题
  * 经典菜单可能出现两条「使用小米互传发送」：一条是管家安装时自己写进 HKLM 的动词，
    一条来自本包的 desktop4:fileExplorerContextMenus。两条都能用；去重需要删管家自己的
    注册表键（等于改管家安装），本项目不做。
  * 日志里会先出现几行 StartShare program files candidate=… exists=False，这是按顺序探测
    Program Files 下的 helper；最后真正用的是 exists=True 那一条，不影响功能。
  * 把 helper 放进 sparse 包目录（ExternalLocation）后发送会失败，根因未定位，
    所以 helper 一律留在包外。

上游版本变更（自 1.0.2）：1.0.6 移除桌面触发角；1.0.10 起支持华为分享与荣耀分享。
