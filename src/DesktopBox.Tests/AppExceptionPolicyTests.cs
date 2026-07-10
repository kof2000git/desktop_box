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

    public static IEnumerable<object[]> RecoverableExceptions() =>
    [
        [new IOException("file unavailable")],
        [new UnauthorizedAccessException("denied")],
        [new COMException("shell failure")],
        [new Win32Exception(5)],
        [new OperationCanceledException()]
    ];
}
