using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DigitalPreservation.Common.Model.DepositArchiver;

public class ArchiveDepositJob
{
    public string? Id { get; set; }

    public string? DepositUri { get; set; }

    public string? DepositId { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    public int? DeletedCount { get; set; } //files deleted from deposit
    public string? Errors { get; set; }

    public string? BatchNumber { get; set; }
}