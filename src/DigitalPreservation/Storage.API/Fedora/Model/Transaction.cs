using System.Net;

namespace Storage.API.Fedora.Model;

public class Transaction
{
    public required Uri Location { get; set; }
    public DateTime Expires { get; set; }
    public HttpStatusCode StatusCode { get; set; }
    public bool CommitStarted { get; set; }
    public bool CommitReturned { get; set; }
    public bool CancelRequested { get; set; }
    public bool Cancelled { get; set; }

    /// <summary>
    /// Set by FedoraTransactionMonitor's pre-commit keep-alive when Fedora no longer recognises
    /// this transaction - expired through a stall, say - so the import loop can stop feeding it and
    /// fail early instead of continuing to write into a transaction that is already gone, or
    /// reaching a commit whose outcome would then be undefined (issue #350).
    /// </summary>
    public bool TransactionLost { get; set; }

    public const string HeaderName = "Atomic-ID";
}