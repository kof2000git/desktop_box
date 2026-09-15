using System.Runtime.ExceptionServices;

namespace DesktopBox.Services;

internal static class StaTaskRunner
{
    public static Task<T> Run<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(action());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    public static T RunSync<T>(Func<T> action, int timeoutMs = 15000)
    {
        T? result = default;
        ExceptionDispatchInfo? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                exception = ExceptionDispatchInfo.Capture(ex);
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        // 离线 UNC / 坏 CLSID 会让 SHParse  hang 数十秒：无限 Join 会永久占锁
        // （IconExtractor 在 lock 内调用），后续系统图标永不更新。15s 超时失败放行。
        if (!thread.Join(timeoutMs))
            throw new TimeoutException($"STA task timed out after {timeoutMs}ms.");
        exception?.Throw();
        return result!;
    }
}
