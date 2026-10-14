using System.Diagnostics;
using System.Net;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;
using Storage.API.Features.Import.Requests;

namespace Storage.API.Tests.Features.Import;

/// <summary>
/// MaintainTransactionState is a TimerCallback, so it has to stay async void - and an exception
/// escaping an async void method terminates the whole process. Before issue #250, three of its
/// awaits (GetTransactionHttpStatus, the commit-not-started KeepTransactionAlive, and a plain throw
/// on a bad timer state) were unguarded, and disposing the monitor's CancellationTokenSource could
/// race a callback parked at one of those awaits and crash the process via CancelAsync throwing
/// ObjectDisposedException from inside async void.
/// </summary>
public class FedoraTransactionMonitorTests
{
    private static Transaction MakeTransaction() => new()
    {
        Location = new Uri("https://fedora.test/rest/tx/abc123")
    };

    [Fact]
    public void MaintainTransactionState_Does_Not_Throw_When_GetTransactionHttpStatus_Fails_And_Commit_Has_Started()
    {
        var fedoraClient = A.Fake<IFedoraClient>();
        var tx = MakeTransaction();
        tx.CommitStarted = true;
        A.CallTo(() => fedoraClient.GetTransactionHttpStatus(tx)).Throws(new InvalidOperationException("boom"));
        var monitor = new FedoraTransactionMonitor(NullLogger.Instance, fedoraClient, tx, Stopwatch.StartNew());

        var act = () => monitor.MaintainTransactionState(tx);

        act.Should().NotThrow();
    }

    [Fact]
    public void MaintainTransactionState_Does_Not_Throw_When_KeepTransactionAlive_Fails_And_Commit_Has_Not_Started()
    {
        var fedoraClient = A.Fake<IFedoraClient>();
        var tx = MakeTransaction();
        tx.CommitStarted = false;
        A.CallTo(() => fedoraClient.KeepTransactionAlive(tx)).Throws(new InvalidOperationException("boom"));
        var monitor = new FedoraTransactionMonitor(NullLogger.Instance, fedoraClient, tx, Stopwatch.StartNew());

        var act = () => monitor.MaintainTransactionState(tx);

        act.Should().NotThrow();
    }

    /// <summary>
    /// Issue #350: a lost transaction was previously only visible in the logs. Recording it on the
    /// transaction itself is what lets ExecuteImportJobHandler's loop notice and stop feeding it,
    /// rather than reaching a commit whose outcome would then be undefined.
    /// </summary>
    [Fact]
    public async Task MaintainTransactionState_Marks_The_Transaction_Lost_When_KeepTransactionAlive_Returns_NonSuccess()
    {
        var fedoraClient = A.Fake<IFedoraClient>();
        var tx = MakeTransaction();
        tx.CommitStarted = false;
        A.CallTo(() => fedoraClient.KeepTransactionAlive(tx))
            .Invokes(() => tx.StatusCode = HttpStatusCode.NotFound)
            .Returns(Task.CompletedTask);
        var monitor = new FedoraTransactionMonitor(NullLogger.Instance, fedoraClient, tx, Stopwatch.StartNew());

        monitor.MaintainTransactionState(tx);
        await WaitUntilAsync(() => tx.TransactionLost, TimeSpan.FromSeconds(2));

        tx.TransactionLost.Should().BeTrue();
    }

    [Fact]
    public async Task MaintainTransactionState_Does_Not_Mark_The_Transaction_Lost_When_KeepTransactionAlive_Succeeds()
    {
        var fedoraClient = A.Fake<IFedoraClient>();
        var tx = MakeTransaction();
        tx.CommitStarted = false;
        A.CallTo(() => fedoraClient.KeepTransactionAlive(tx))
            .Invokes(() => tx.StatusCode = HttpStatusCode.NoContent)
            .Returns(Task.CompletedTask);
        var monitor = new FedoraTransactionMonitor(NullLogger.Instance, fedoraClient, tx, Stopwatch.StartNew());

        monitor.MaintainTransactionState(tx);
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        tx.TransactionLost.Should().BeFalse();
    }

    [Fact]
    public void MaintainTransactionState_Does_Not_Throw_When_The_Timer_State_Is_Not_The_Transaction()
    {
        var fedoraClient = A.Fake<IFedoraClient>();
        var tx = MakeTransaction();
        var monitor = new FedoraTransactionMonitor(NullLogger.Instance, fedoraClient, tx, Stopwatch.StartNew());

        var act = () => monitor.MaintainTransactionState(new object());

        act.Should().NotThrow();
    }

    [Fact]
    public async Task A_Callback_Resuming_After_Dispose_Does_Not_Throw_And_Still_Requests_Cancel()
    {
        // The monitor is disposed while the callback is parked at GetTransactionHttpStatus's await -
        // exactly the race a mechanical "just dispose the CTS after the timer" fix would crash on.
        var fedoraClient = A.Fake<IFedoraClient>();
        var tx = MakeTransaction();
        tx.CommitStarted = true;
        var statusTcs = new TaskCompletionSource<HttpStatusCode>();
        A.CallTo(() => fedoraClient.GetTransactionHttpStatus(tx)).Returns(statusTcs.Task);
        var monitor = new FedoraTransactionMonitor(NullLogger.Instance, fedoraClient, tx, Stopwatch.StartNew());

        monitor.MaintainTransactionState(tx); // parks at the GetTransactionHttpStatus await
        monitor.Dispose();
        statusTcs.SetResult(HttpStatusCode.NotFound); // non-2xx: resumes onto the cancel path

        await WaitUntilAsync(() => tx.CancelRequested, TimeSpan.FromSeconds(2));

        tx.CancelRequested.Should().BeTrue(
            "the cancel request is recorded before the disposed monitor is asked to cancel its token");
    }

    /// <summary>
    /// Issue #383 review point 1: a GET on a transaction that is mid-commit answers 409, and the
    /// previous version treated any non-2xx - 409 included - as grounds to cancel. That cancelled
    /// commits that were simply still running. 409 specifically must not cancel.
    /// </summary>
    [Fact]
    public async Task A_409_Status_While_Committing_Does_Not_Cancel()
    {
        var fedoraClient = A.Fake<IFedoraClient>();
        var tx = MakeTransaction();
        tx.CommitStarted = true;
        A.CallTo(() => fedoraClient.GetTransactionHttpStatus(tx)).Returns(HttpStatusCode.Conflict);
        var monitor = new FedoraTransactionMonitor(NullLogger.Instance, fedoraClient, tx, Stopwatch.StartNew());

        monitor.MaintainTransactionState(tx);
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        tx.CancelRequested.Should().BeFalse();
        tx.Cancelled.Should().BeFalse();
    }

    /// <summary>
    /// Issue #383 review point 1: a keep-alive POSTed against a transaction that is mid-commit is
    /// itself what Fedora answers with 409 - which is what was cancelling commits that were simply
    /// still running. The commit-started branch must only GET the status, never POST a keep-alive.
    /// </summary>
    [Fact]
    public async Task No_Keep_Alive_Is_Posted_While_Committing()
    {
        var fedoraClient = A.Fake<IFedoraClient>();
        var tx = MakeTransaction();
        tx.CommitStarted = true;
        A.CallTo(() => fedoraClient.GetTransactionHttpStatus(tx)).Returns(HttpStatusCode.OK);
        var monitor = new FedoraTransactionMonitor(NullLogger.Instance, fedoraClient, tx, Stopwatch.StartNew());

        monitor.MaintainTransactionState(tx);
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        A.CallTo(() => fedoraClient.KeepTransactionAlive(tx)).MustNotHaveHappened();
        tx.CancelRequested.Should().BeFalse();
    }

    /// <summary>
    /// Issue #383 review point 6: the issue scoped a lost transaction to 404/410. The previous
    /// version flagged it on any non-2xx, so a transient 502/503/504 from the ALB on a single
    /// pre-commit keep-alive would have ended a multi-hour import over one blip.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task A_Transient_Error_Before_Commit_Does_Not_Mark_The_Transaction_Lost(HttpStatusCode transientStatus)
    {
        var fedoraClient = A.Fake<IFedoraClient>();
        var tx = MakeTransaction();
        tx.CommitStarted = false;
        A.CallTo(() => fedoraClient.KeepTransactionAlive(tx))
            .Invokes(() => tx.StatusCode = transientStatus)
            .Returns(Task.CompletedTask);
        var monitor = new FedoraTransactionMonitor(NullLogger.Instance, fedoraClient, tx, Stopwatch.StartNew());

        monitor.MaintainTransactionState(tx);
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        tx.TransactionLost.Should().BeFalse();
    }

    [Fact]
    public async Task A_Non_2xx_Status_Cancels_The_Token_CommitTransaction_Observes_Without_Disposal()
    {
        var fedoraClient = A.Fake<IFedoraClient>();
        var tx = MakeTransaction();
        tx.CommitStarted = true;
        A.CallTo(() => fedoraClient.GetTransactionHttpStatus(tx)).Returns(HttpStatusCode.NotFound);

        // Stands in for the real HTTP call: stays pending until the token it was given is
        // cancelled, same as the real fedoraClient.CommitTransaction observing cancellation.
        var commitBody = new TaskCompletionSource();
        var observedToken = default(CancellationToken);
        A.CallTo(() => fedoraClient.CommitTransaction(tx, A<CancellationToken>._))
            .Invokes((Transaction _, CancellationToken token) =>
            {
                observedToken = token;
                token.Register(() => commitBody.TrySetCanceled(token));
            })
            .Returns(commitBody.Task);
        var monitor = new FedoraTransactionMonitor(NullLogger.Instance, fedoraClient, tx, Stopwatch.StartNew());

        var commitTask = monitor.CommitTransaction();
        observedToken.IsCancellationRequested.Should().BeFalse();

        monitor.MaintainTransactionState(tx);

        await commitTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        observedToken.IsCancellationRequested.Should().BeTrue(
            "the token CommitTransaction passed to fedoraClient.CommitTransaction must observe the cancel");
        tx.Cancelled.Should().BeTrue();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }
}
