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

    public int? DeletedCount { get; set; }
    public string? Errors { get; set; }

    public string? BatchNumber { get; set; }
    /*
         id                text                     not null
           constraint pk_archive_jobs
               primary key,
       deposit_uri       text                     not null,
       deposit_id        text                     not null,
       start_time        timestamp with time zone not null,
       end_time          timestamp with time zone,
       deleted_count     int,
       errors            text,
       batch_number      int
     */
}
