# GameTimeRecord

GameTimeRecord 是一款面向 Windows 10 及更高版本的本地游戏时间记录工具，用于记录没有内置游玩时间统计的游戏。

## 当前功能

- 新建、编辑和删除游戏资料
- 多个游戏同时计时
- 开始、暂停、恢复和结束游玩
- 以秒为精度保存完整操作时间
- 修改历史操作时间并重新计算统计结果
- 统计累计游玩秒数、游玩次数、首次游玩时间和最后游玩时间
- 单独复制四项统计值
- 使用 SQLite 将数据保存在 `%LOCALAPPDATA%\GameTimeRecord\game-time-record.db`

应用退出前，仍在计时的游戏需要先暂停或结束。暂停状态可以保留到下次启动，暂停期间不计入游玩时长。

## 下载

从 [GitHub Releases](https://github.com/huaxianyan/GameTimeRecord/releases) 下载 `GameTimeRecord-win-x64.zip`，解压后运行 `GameTimeRecord.exe`。发布包已经包含运行环境，不需要另外安装 .NET。

## 开发

需要 .NET 10 SDK `10.0.302` 或兼容的后续补丁版本。

```powershell
dotnet restore
dotnet run --project src/GameTimeRecord.App
dotnet test tests/GameTimeRecord.Tests/GameTimeRecord.Tests.csproj -c Release
```

构建 Windows x64 便携包：

```powershell
./scripts/publish-windows.ps1
```

## 项目结构

- `src/GameTimeRecord.Core`：游玩会话规则和统计计算
- `src/GameTimeRecord.App`：WPF 界面与 SQLite 存储
- `tests/GameTimeRecord.Tests`：领域规则和真实 SQLite 入口测试
