# 墨序 · Moxu

深黑与暖金配色的 Windows 每日规划软件。用仿宋写下安排，在横线随记页记录生活。

![墨序界面（示例内容）](docs/preview.png)

## 使用

下载整个仓库的 ZIP，解压到可写文件夹（例如 D 盘），双击 `portable/Moxu.exe`。
也可以在该文件页面点击下载原始文件。无需账号、联网或安装向导。

- 点日历或周视图，切换今天、未来或过去的日期。
- 输入安排，按 Enter 或点加号添加。
- 点击任务文字，展开修改标题、时间、重点和备注。
- 勾选完成；删除后可以撤销删除。
- 在右侧「随记」的横线页写文字，每个日期单独保存。
- 输入后约半秒自动保存；Ctrl + S 立即保存；Ctrl + N 聚焦添加框。
- 再次双击程序会请求唤回已经打开的窗口。

## 数据

运行后，在可执行文件旁创建 `Data` 文件夹：

- `plans.json`：计划和日记。
- `plans.json.bak`：上一次保存的备份。
- `session.lock`：运行时占用锁。

备份时先关闭软件，再复制整个 `Data` 文件夹。请勿仅移动程序而遗漏数据。
这些文件已加入 Git 忽略规则。仓库不包含个人安排、日记、账号或密钥；界面截图使用示例内容。

## 环境与字体

Windows 10/11，.NET Framework 4.x，WPF。程序使用系统字体，不捆绑字体文件。
默认字体为系统仿宋；安装有可识别的方正仿宋时，正文会优先使用它。

## 从源码构建

在 Windows PowerShell 中，从仓库根目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

生成 `build/Moxu.exe`。构建使用 Windows 自带的 .NET Framework C# 编译器，不需要下载 npm 包。

图标由 `make-icon.ps1` 绘制，修改后先运行该脚本，再重新构建。

## 检查

程序包含独立的 `--test` 模式，覆盖中文和多行文本保存、跨年日期、完成状态、备份恢复、界面添加与编辑、日期切换、删除撤销和重新读取。测试目录必须使用新的空目录。

```powershell
$testDir = Join-Path $env:TEMP ('moxu-test-' + [Guid]::NewGuid().ToString('N'))
$p = Start-Process .\build\Moxu.exe -ArgumentList @('--test', ('"' + $testDir + '"')) -PassThru -Wait
$p.ExitCode
Get-Content (Join-Path $testDir 'passed.txt')
```

## 当前范围

这是个人桌面软件的早期版本，数据仅保存在本机。随记支持文字，不提供绘画或云同步。程序尚未进行代码签名。