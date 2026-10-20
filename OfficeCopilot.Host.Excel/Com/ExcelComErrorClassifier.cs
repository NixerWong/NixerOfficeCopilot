using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace OfficeCopilot.Host.Excel.Com
{
    internal static class ExcelComErrorClassifier
    {
        internal const int RpcECallRejected = unchecked((int)0x80010001);
        internal const int RpcEServerCallRetryLater = unchecked((int)0x8001010A);
        internal const int ExcelVbaBusy = unchecked((int)0x800AC472);

        public static bool IsBusyHResult(int hresult)
        {
            return hresult == RpcECallRejected
                || hresult == RpcEServerCallRetryLater
                || hresult == ExcelVbaBusy;
        }

        public static bool TryGetBusyHResult(Exception exception, out int hresult)
        {
            hresult = 0;
            if (exception == null)
            {
                return false;
            }

            var pending = new Stack<Exception>();
            pending.Push(exception);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                var comException = current as COMException;
                if (comException != null && IsBusyHResult(comException.ErrorCode))
                {
                    hresult = comException.ErrorCode;
                    return true;
                }

                var aggregate = current as AggregateException;
                if (aggregate != null)
                {
                    foreach (var inner in aggregate.InnerExceptions)
                    {
                        if (inner != null)
                        {
                            pending.Push(inner);
                        }
                    }
                }

                if (current.InnerException != null)
                {
                    pending.Push(current.InnerException);
                }
            }

            return false;
        }

        public static bool IsBusyErrorCode(string errorCode)
        {
            return string.Equals(
                errorCode,
                ExcelHostBusyException.ErrorCode,
                StringComparison.Ordinal);
        }

        public static bool IsBusyException(Exception exception)
        {
            int hresult;
            return TryGetBusyHResult(exception, out hresult);
        }
    }

    internal sealed class ExcelHostBusyException : InvalidOperationException
    {
        public const string ErrorCode = "excel.host_busy";

        public ExcelHostBusyException(Exception innerException)
            : base(
                "Excel 正处于单元格编辑状态或有对话框打开，请按 Esc 退出编辑或关闭对话框后重试。",
                innerException)
        {
        }
    }
}
