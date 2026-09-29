using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using DesktopBox;
using FluentAssertions;

namespace DesktopBox.Tests;

public class AppExceptionPolicyTests
{
    [Theory]
    [MemberData(nameof(RecoverableExceptions))]
    public void RecoverableDispatcherErrors_CanContinue(Exception exception)
    {
        App.CanContinueAfterDispatcherException(exception).Should().BeTrue();
    }

    [Fact]
    public void UnknownProgrammingError_DoesNotContinue()
    {
        App.CanContinueAfterDispatcherException(new InvalidOperationException("broken invariant"))
            .Should().BeFalse();
    }

    [Fact]
    public void DispatcherErrorMessage_DistinguishesRecoverableAndFatalFailures()
    {
        App.GetDispatcherExceptionMessageKey(new IOException("temporary"))
            .Should().Be("dialog.unhandledError");
        App.GetDispatcherExceptionMessageKey(new InvalidOperationException("broken invariant"))
            .Should().Be("dialog.fatalError");
    }

    [Fact]
    public void BenignShutdownCrash_IdentifiedCorrectly()
    {
        // 模拟事件日志中记录的 CRT ModuleUninitializer 退出异常
        var exWithStack = new CustomStackTraceDllNotFoundException(
            "Dll was not found.",
            "   at __std_type_info_destroy_list(__type_info_node*)\r\n" +
            "   at __scrt_uninitialize_type_info()\r\n" +
            "   at _app_exit_callback()\r\n" +
            "   at <CrtImplementationDetails>.ModuleUninitializer.SingletonDomainUnload(Object source, EventArgs arguments)");

        App.IsBenignShutdownCrash(exWithStack).Should().BeTrue();
        App.IsBenignShutdownCrash(new DllNotFoundException("missing some other dll")).Should().BeFalse();
        App.IsBenignShutdownCrash(new InvalidOperationException("test")).Should().BeFalse();
        App.IsBenignShutdownCrash(null).Should().BeFalse();
    }

    [Fact]
    public void LogService_EnqueueAndFlush_DoesNotThrow()
    {
        var action = () =>
        {
            DesktopBox.Services.LogService.Info("Test", "info message");
            DesktopBox.Services.LogService.Warn("Test", "warn message");
            DesktopBox.Services.LogService.Error(new Exception("test ex"), "Test", "error context");
            DesktopBox.Services.LogService.Flush();
        };
        action.Should().NotThrow();
    }

    private class CustomStackTraceDllNotFoundException : DllNotFoundException
    {
        private readonly string _stackTrace;
        public CustomStackTraceDllNotFoundException(string message, string stackTrace) : base(message)
        {
            _stackTrace = stackTrace;
        }
        public override string StackTrace => _stackTrace;
    }

    public static IEnumerable<object[]> RecoverableExceptions() =>
    [
        [new IOException("file unavailable")],
        [new UnauthorizedAccessException("denied")],
        [new COMException("shell failure")],
        [new Win32Exception(5)],
        [new OperationCanceledException()]
    ];
}
