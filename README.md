# SubtitleMask 字幕遮挡条

播放双语字幕视频时，用一条置顶黑条盖住中文那一行字幕。

## 功能

- 双击即用，免安装，只依赖系统自带的 .NET Framework 4.x
- 置顶置黑条，可拖动位置、拉伸大小
- `Ctrl` + 滚轮以 5% 步进调整不透明度（25%–100%），右键菜单另有固定档位
- 高度预设 40 / 56 / 72 / 90 px
- 位置、大小、不透明度自动保存（优先 `exe` 同目录的 `SubtitleMask.ini`，目录不可写时回退到 `%APPDATA%\SubtitleMask\settings.ini`）
- 启动不抢播放器焦点

## 使用方法

1. 运行 `SubtitleMask.exe`，屏幕底部中央出现黑条。
2. 按住黑条内部拖动可移动；拖上下左右边缘或四角可调整大小（避开中心 X 附近）。
3. 鼠标移到黑条上，中心浮现 `X`，点击关闭。
4. 悬停黑条按住 `Ctrl` 滚动滚轮调整透明度。
5. 右键菜单：不透明度、高度预设、始终显示关闭按钮、重置位置和大小、退出。

## 注意事项

播放器请使用 **窗口化** 或 **无边框全屏** 模式。部分播放器的「独占全屏」会绕过 Windows 桌面合成，任何置顶窗口（包括本工具）都会被盖住。

## 系统要求

- Windows 10 / 11
- .NET Framework 4.x（系统自带）

## 从源码编译

需要 Windows 自带的 .NET Framework 编译器 `csc.exe`：

```
src\build.cmd
```

双击运行即可，输出 `SubtitleMask.exe` 到仓库根目录。

## 源码结构

| 文件 | 说明 |
| --- | --- |
| `src/SubtitleMask.cs` | 程序源码（C# / WinForms） |
| `src/build.cmd` | 编译脚本 |
| `src/app.manifest` | 高 DPI 声明与兼容性清单 |
| `src/icon.ico` | 应用图标 |
| `src/make-icon.ps1` | 图标生成脚本 |

## 许可证

MIT
