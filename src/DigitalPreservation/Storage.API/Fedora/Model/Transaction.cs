using System.Net;

namespace Storage.API.Fedora.Model;

public class Transaction
{
    public required Uri Location { get; set; }
    public DateTime Expires { get; set; }

    /// <summary>
    /// The transaction's status as last reported by a keep-alive, a plain status GET, or a
    /// rollback - everything EXCEPT the commit PUT itself. Kept separate from
    /// <see cref="CommitStatusCode"/> so a keep-alive response that lands after the commit has
    /// already returned cannot overwrite the commit's own outcome (issue #383 review point 5).
    /// </summary>
    public HttpStatusCode StatusCode { get; set; }

    /// <summary>
    /// The commit PUT's own HTTP status, written only by FedoraClient.CommitTransaction. See the
    /// remarks on <see cref="StatusCode"/> for why this is a separate field.
    /// </summary>
    public HttpStatusCode CommitStatusCode { get; set; }

    public bool CommitStarted { get; set; }
    public bool CommitReturned { get; set; }
    public bool CancelRequested { get; set; }
    public bool Cancelled { get; set; }

    private volatile bool transactionLost;

    /// <summary>
    /// Set by FedoraTransactionMonitor's pre-commit keep-alive when Fedora no longer recognises
    /// this transaction - expired through a stall, say - so the import loop can stop feeding it and
    /// fail early instead of continuing to write into a transaction that is already gone, or
    /// reaching a commit whose outcome would then be undefined (issue #350). Backed by a volatile
    /// field: written by the timer callback's thread, read by the import loop's thread (issue #383
    /// review nit).
    /// </summary>
    public bool TransactionLost
    {
        get => transactionLost;
        set => transactionLost = value;
    }

    public const string HeaderName = "Atomic-ID";
}