using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace OfficeCopilot.Host.PowerPoint
{
    /// <summary>
    /// 把 PowerPoint COM 调用封送到创建适配器的 UI 线程，并对 PowerPoint 忙碌时的
    /// RPC_E_CALL_REJECTED / RPC_E_SERVERCALL_RETRYLATER 做有限次退避重试。
    /// 工具经由 HostBridge 管道在后台线程执行，直接跨线程访问 COM 会在 PowerPoint 忙时被拒绝。
    /// </summary>
    internal sealed class PowerPointComDispatcher
    {
        internal const int RpcECallRejected = unchecked((int)0x80010001);
        internal const int RpcEServerCallRetryLater = unchecked((int)0x8001010A);

        private const int MaxAttempts = 8;
        private const int InitialDelayMilliseconds = 100;

        private readonly SynchronizationContext _context;
        private readonly int _ownerThreadId;

        public PowerPointComDispatcher()
            : this(SynchronizationContext.Current)
        {
        }

        public PowerPointComDispatcher(SynchronizationContext context)
        {
            _context = context;
            _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public T Invoke<T>(Func<T> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (_context == null || Thread.CurrentThread.ManagedThreadId == _ownerThreadId)
            {
                return InvokeWithRetry(action);
            }

            T result = default(T);
            ExceptionDispatchInfo failure = null;
            _context.Send(
                _ =>
                {
                    try
                    {
                        result = InvokeWithRetry(action);
                    }
                    catch (Exception ex)
                    {
                        failure = ExceptionDispatchInfo.Capture(ex);
                    }
                },
                null);

            failure?.Throw();
            return result;
        }

        public void Invoke(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            Invoke<object>(() =>
            {
                action();
                return null;
            });
        }

        internal static T InvokeWithRetry<T>(Func<T> action)
        {
            var delay = InitialDelayMilliseconds;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return action();
                }
                catch (COMException ex) when (IsRetryable(ex.ErrorCode))
                {
                    if (attempt >= MaxAttempts)
                    {
                        throw new PowerPointHostBusyException(ex);
                    }

                    Thread.Sleep(delay);
                    delay = Math.Min(delay * 2, 2000);
                }
            }
        }

        internal static bool IsRetryable(int hresult)
        {
            return hresult == RpcECallRejected || hresult == RpcEServerCallRetryLater;
        }

    }

    /// <summary>PowerPoint 持续拒绝 COM 调用（正在编辑文本、打开了模态对话框等），重试已用尽。</summary>
    public sealed class PowerPointHostBusyException : InvalidOperationException
    {
        public PowerPointHostBusyException(Exception inner)
            : base("PowerPoint 正忙（可能正在编辑文本或有对话框打开），请退出编辑或关闭对话框后重试。", inner)
        {
        }
    }

    /// <summary>
    /// 通过 IDispatch 晚绑定访问返回 Office.Core 类型（MsoTriState / MsoShapeType）的成员，
    /// 以免编译期依赖 office.dll。
    /// </summary>
    internal static class ComLate
    {
        public static object Get(object target, string name)
        {
            if (target == null)
            {
                return null;
            }

            return Invoke(target, name, BindingFlags.GetProperty, null);
        }

        public static object Get(object target, string name, params object[] args)
        {
            if (target == null) return null;
            return Invoke(target, name, BindingFlags.GetProperty, args);
        }

        private const int DispEUnknownName = unchecked((int)0x80020006);

        private static object Invoke(object target, string name, BindingFlags flags, object[] args)
        {
            try
            {
                return target.GetType().InvokeMember(name, flags, null, target, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
            catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == DispEUnknownName)
            {
                throw new MissingMemberException("COM 对象没有成员 " + name + "。", ex);
            }
        }

        public static void Set(object target, string name, object value)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            Invoke(target, name, BindingFlags.SetProperty, new[] { value });
        }

        public static int GetInt(object target, string name, int fallback = 0)
        {
            try
            {
                var value = Get(target, name);
                return value == null ? fallback : Convert.ToInt32(value);
            }
            catch
            {
                return fallback;
            }
        }

        /// <summary>MsoTriState：msoTrue = -1，msoFalse = 0。</summary>
        public static bool GetTri(object target, string name)
        {
            return GetInt(target, name) != 0;
        }

        public static object Call(object target, string name, params object[] args)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return Invoke(target, name, BindingFlags.InvokeMethod, args);
        }
    }
}
