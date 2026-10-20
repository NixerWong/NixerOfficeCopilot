using System;
using System.Diagnostics;
using System.Threading;

namespace OfficeCopilot.Diagnostics.Logging
{
    /// <summary>
    /// 观测用：后台线程周期性地向 UI 线程投递空回调，并记录响应延迟超过阈值的时段。只写日志，不改变行为。
    /// 日志 stage 为 ui-hang，latencyMs 为回调等待时长（即 UI 线程被占用的时长）。
    /// </summary>
    public sealed class UiResponsivenessProbe : IDisposable
    {
        private const int IntervalMilliseconds = 50;
        private const int HangThresholdMilliseconds = 200;

        private readonly Action<Action> _postToUi;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();

        private UiResponsivenessProbe(Action<Action> postToUi)
        {
            _postToUi = postToUi;
        }

        /// <summary>
        /// 启动探针。Office 的 Startup 阶段 <see cref="SynchronizationContext.Current"/> 为空，
        /// 因此由调用方提供能投递到 UI 线程的委托（例如隐藏控件的 BeginInvoke）。
        /// </summary>
        public static UiResponsivenessProbe Start(Action<Action> postToUi)
        {
            var probe = new UiResponsivenessProbe(postToUi);
            var thread = new Thread(probe.Run)
            {
                IsBackground = true,
                Name = "UiResponsivenessProbe"
            };
            thread.Start();
            StartupTimingLog.Mark("ui-probe-started");
            return probe;
        }

        private void Run()
        {
            var token = _cancellation.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    using (var responded = new ManualResetEventSlim(false))
                    {
                        var stopwatch = Stopwatch.StartNew();
                        _postToUi(() => responded.Set());
                        responded.Wait(token);

                        var latency = stopwatch.ElapsedMilliseconds;
                        if (latency > HangThresholdMilliseconds)
                        {
                            StartupTimingLog.Mark("ui-hang latencyMs=" + latency.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                    }

                    token.WaitHandle.WaitOne(IntervalMilliseconds);
                }
            }
            catch (Exception)
            {
                // 探针只用于观测；关闭时 UI 对象可能已释放，任何异常都直接结束，不影响宿主进程。
            }
        }

        public void Dispose()
        {
            _cancellation.Cancel();
        }
    }
}
