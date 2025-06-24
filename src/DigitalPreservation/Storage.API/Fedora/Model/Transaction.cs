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

    public const string HeaderName = "Atomic-ID";
}