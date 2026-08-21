using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace XFramework
{
    /// <summary>一条待执行的外部命令。</summary>
    public readonly struct SpacetimeCommand
    {
        public readonly string Label;
        public readonly string Executable;
        public readonly string Arguments;
        public readonly string WorkingDirectory;

        public SpacetimeCommand(string label, string executable, string arguments, string workingDirectory)
        {
            Label = label;
            Executable = executable;
            Arguments = arguments;
            WorkingDirectory = workingDirectory;
        }
    }

    /// <summary>
    /// 按顺序跑一串外部命令，把输出逐行喂给回调。
    ///
    /// 几个必须这么写的原因：
    ///   - Process 的输出事件在<b>后台线程</b>触发，不能直接碰 Unity API。
    ///     所以先进 ConcurrentQueue，再由 EditorApplication.update 在主线程排空。
    ///   - spacetime CLI 会输出 ANSI 颜色转义（publish 的迁移计划就是彩色的），
    ///     Unity 的 GUI 不认，直接显示会是一堆乱码，所以要剥掉。
    ///   - 中文输出要显式指定 UTF8，否则在中文 Windows 上会乱码。
    ///   - `logs --follow` 是不会自己结束的，必须能 Kill。
    /// </summary>
    public class SpacetimeCliRunner
    {
        private static readonly Regex AnsiEscape = new Regex(
            @"\x1B\[[0-9;]*[A-Za-z]|\x1B\][^\x07]*\x07", RegexOptions.Compiled);

        private readonly ConcurrentQueue<string> _pending = new ConcurrentQueue<string>();
        private readonly Queue<SpacetimeCommand> _queue = new Queue<SpacetimeCommand>();

        private Process _process;
        private Action<string> _onLine;
        private Action<bool> _onAllDone;
        private bool _updateHooked;
        private bool _anyFailed;

        /// <summary>是否有命令正在跑。</summary>
        public bool IsRunning => _process != null && !_process.HasExited;

        /// <summary>当前正在跑的命令标签，空闲时为空串。</summary>
        public string CurrentLabel { get; private set; } = string.Empty;

        /// <summary>
        /// 排入并开始执行一串命令。任一步失败则中止后续步骤。
        /// </summary>
        /// <param name="commands">按顺序执行的命令。</param>
        /// <param name="onLine">每行输出的回调，保证在主线程。</param>
        /// <param name="onAllDone">全部结束时回调，参数为是否全部成功。</param>
        public void Run(IEnumerable<SpacetimeCommand> commands, Action<string> onLine, Action<bool> onAllDone)
        {
            if (IsRunning)
            {
                onLine?.Invoke("[工具] 已有命令在执行，忽略这次请求。");
                return;
            }

            _onLine = onLine;
            _onAllDone = onAllDone;
            _anyFailed = false;

            _queue.Clear();
            foreach (SpacetimeCommand command in commands)
            {
                _queue.Enqueue(command);
            }

            HookUpdate();
            StartNext();
        }

        /// <summary>杀掉当前进程并清空后续队列。用于停掉 `logs --follow`。</summary>
        public void Stop()
        {
            _queue.Clear();

            if (_process == null)
            {
                return;
            }

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                }
            }
            catch (Exception e)
            {
                Enqueue($"[工具] 终止进程失败：{e.Message}");
            }
        }

        private void StartNext()
        {
            if (_queue.Count == 0)
            {
                CurrentLabel = string.Empty;
                _process = null;
                Enqueue(_anyFailed ? "[工具] 执行结束（有失败）。" : "[工具] 执行结束。");
                // 完成回调也要走队列，保证在主线程、且排在所有输出之后
                Enqueue(DoneMarker);
                return;
            }

            SpacetimeCommand command = _queue.Dequeue();
            CurrentLabel = command.Label;

            var startInfo = new ProcessStartInfo
            {
                FileName = command.Executable,
                Arguments = command.Arguments,
                WorkingDirectory = command.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            Enqueue($"[工具] ▶ {command.Label}");
            Enqueue($"[工具]   {command.Executable} {command.Arguments}");

            try
            {
                _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                _process.OutputDataReceived += (_, args) => Enqueue(args.Data);
                _process.ErrorDataReceived += (_, args) => Enqueue(args.Data);
                _process.Exited += OnProcessExited;

                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                _anyFailed = true;
                Enqueue($"[工具] 启动失败：{e.Message}");
                _process = null;
                _queue.Clear();
                CurrentLabel = string.Empty;
                Enqueue(DoneMarker);
            }
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            // 注意：这里是后台线程，只能往队列里塞东西
            Process finished = sender as Process;
            int exitCode = -1;
            try
            {
                if (finished != null)
                {
                    exitCode = finished.ExitCode;
                }
            }
            catch
            {
                // 进程已被回收，拿不到退出码，按失败处理
            }

            if (exitCode == 0)
            {
                Enqueue("[工具] ✔ 完成");
            }
            else
            {
                _anyFailed = true;
                Enqueue($"[工具] ✘ 退出码 {exitCode}");
                _queue.Clear();
            }

            Enqueue(NextMarker);
        }

        private const string DoneMarker = "__STDB_ALL_DONE__";
        private const string NextMarker = "__STDB_NEXT__";

        private void Enqueue(string line)
        {
            if (line == null)
            {
                return;
            }
            _pending.Enqueue(line);
        }

        private void HookUpdate()
        {
            if (_updateHooked)
            {
                return;
            }
            EditorApplication.update += Drain;
            _updateHooked = true;
        }

        private void UnhookUpdate()
        {
            if (!_updateHooked)
            {
                return;
            }
            EditorApplication.update -= Drain;
            _updateHooked = false;
        }

        /// <summary>在主线程排空输出队列。</summary>
        private void Drain()
        {
            while (_pending.TryDequeue(out string line))
            {
                if (line == NextMarker)
                {
                    StartNext();
                    continue;
                }

                if (line == DoneMarker)
                {
                    UnhookUpdate();
                    Action<bool> done = _onAllDone;
                    _onAllDone = null;
                    done?.Invoke(!_anyFailed);
                    continue;
                }

                _onLine?.Invoke(Sanitize(line));
            }
        }

        /// <summary>剥掉 ANSI 转义序列和回车。</summary>
        public static string Sanitize(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return string.Empty;
            }
            return AnsiEscape.Replace(line, string.Empty).Replace("\r", string.Empty);
        }
    }
}
